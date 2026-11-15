using AdaptiveBossArena.Core.Constants;
using AdaptiveBossArena.Core.Services;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AdaptiveBossArena.UI
{
    /// <summary>
    /// The settings panel: audio, accessibility and control rebinding, applied live and persisted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reachable from both the title screen and the pause menu, and present in both scenes, because
    /// it is also the thing that <em>applies</em> the saved settings when a scene loads — not only
    /// the thing that edits them. A player who set their preferences once expects them honoured every
    /// time the game starts, whether or not they open this panel again.
    /// </para>
    /// <para>
    /// It reaches the systems it controls through the narrowest seam available for each: audio and
    /// screen-shake through their service interfaces, and the two accessibility flags through the
    /// global <see cref="FeedbackSettings"/> that the combat feedback reads. That keeps this menu
    /// from having to hold a reference to every hit flash and camera in the scene.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SettingsMenu : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0.03f, 0.03f, 0.05f, 0.985f);
        private static readonly Color DevPanelColor = new Color(0.02f, 0.02f, 0.04f, 0.995f);
        private static readonly Color Accent = new Color(0.45f, 0.85f, 0.95f);
        private static readonly Color ToggleAccent = new Color(0.55f, 0.9f, 0.6f);
        private static readonly Color MutedText = new Color(0.72f, 0.76f, 0.82f);
        private static readonly Color SectionHeaderColor = new Color(0.95f, 0.75f, 0.45f);

        /// <summary>Hold multiplier applied when the extended-tell accessibility option is on.</summary>
        private const float ExtendedTellMultiplier = 2f;

        /// <summary>Name of the gameplay action map, where the rebindable controls live.</summary>
        private const string GameplayMapName = "Gameplay";

        [Header("Panel")]
        [SerializeField]
        [Tooltip("Root object shown while the settings are open.")]
        private GameObject _panel;

        [Header("Audio")]
        [SerializeField]
        private Slider _masterSlider;

        [SerializeField]
        private Slider _musicSlider;

        [SerializeField]
        private Slider _effectsSlider;

        [Header("Accessibility")]
        [SerializeField]
        private Slider _shakeSlider;

        [SerializeField]
        private Toggle _reducedFlashingToggle;

        [SerializeField]
        private Toggle _extendedTellToggle;

        [Header("Developer Options")]
        [SerializeField]
        [Tooltip("Read-only developer options and rules viewer.")]
        private GameObject _developerRulesPanel;

        [Header("Input")]
        [SerializeField]
        [Tooltip("The shared input actions asset, so rebindings reach the controls the game reads.")]
        private InputActionAsset _actions;

        private ISaveService _saveService;
        private SettingsData _settings = new SettingsData();

        private InputActionMap _gameplayMap;
        private RebindButton[] _rebindRows;
        private InputActionRebindingExtensions.RebindingOperation _activeRebind;
        private Coroutine _rebindCoroutine;

        // Set while control values are being written from loaded data, so the resulting change
        // callbacks do not treat a programmatic sync as a player edit and save over themselves.
        private bool _isSyncing;

        /// <summary>True while the settings panel is open.</summary>
        public bool IsOpen => _panel != null && _panel.activeSelf;

        private void Awake()
        {
            if (_saveService == null && ServiceRegistry.Current != null)
            {
                ServiceRegistry.Current.TryGet(out _saveService);
            }
            _settings = Load();
        }

        private void Start()
        {
            if (_saveService == null && ServiceRegistry.Current != null)
            {
                ServiceRegistry.Current.TryGet(out _saveService);
            }

            _settings = Load();

            EnsureRuntimeUi();

            ApplyInputOverrides();
            ApplyAll();
            SyncControls();

            _gameplayMap = _actions != null ? _actions.FindActionMap(GameplayMapName, throwIfNotFound: false) : null;
            _rebindRows = GetComponentsInChildren<RebindButton>(includeInactive: true);
            RefreshRebindLabels();

            Close();
        }

        private void OnDestroy()
        {
            // An operation left running past this component's life keeps a reference to a destroyed
            // callback target, so it must be disposed rather than abandoned.
            _activeRebind?.Dispose();
            _activeRebind = null;
        }

        /// <summary>
        /// Shows the settings panel, on top of whatever it was opened from.
        /// </summary>
        /// <remarks>
        /// The panel is raised to the last sibling every time rather than relying on where it happened
        /// to be built. Unity draws later siblings over earlier ones, and this panel is opened from
        /// screens created after it — the title's buttons, the pause menu — which would otherwise draw
        /// straight through it no matter how opaque its background is.
        /// </remarks>
        public void Open()
        {
            EnsureRuntimeUi();
            RaiseToTop();

            if (_panel != null)
            {
                _panel.SetActive(true);
            }
        }

        /// <summary>
        /// Ensures all modern controls (Jump, Parry, Guard, Special, Heal, Swap, Lock-On, Camera)
        /// and the read-only Developer Options & Rules are built dynamically at runtime.
        /// </summary>
        public void EnsureRuntimeUi()
        {
            if (_panel == null)
            {
                Transform found = transform.Find("SettingsPanel");
                if (found != null)
                {
                    _panel = found.gameObject;
                }
                else
                {
                    Transform canvas = transform.root.GetComponentInChildren<Canvas>(true)?.transform ?? transform;
                    _panel = CreateFullScreenPanel(canvas, "SettingsPanel", PanelColor);
                }
            }

            Transform existingDevPanel = _panel.transform.Find("DeveloperRulesPanel");
            Transform existingJump = _panel.transform.Find("Jump Rebind");
            Transform existingParry = _panel.transform.Find("Parry Strike Rebind");
            Transform existingGuard = _panel.transform.Find("Guard / Deflect Rebind");
            Transform existingDevBtn = _panel.transform.Find("DevRulesBtn");

            if (existingDevPanel != null && existingJump != null && existingParry != null && existingGuard != null && existingDevBtn != null)
            {
                _developerRulesPanel = existingDevPanel.gameObject;
                if (_rebindRows == null || _rebindRows.Length == 0)
                {
                    _rebindRows = _panel.GetComponentsInChildren<RebindButton>(true);
                }
                return;
            }

            // Clean out legacy child elements from the old scene
            for (int i = _panel.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = _panel.transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }

            Image panelImage = _panel.GetComponent<Image>();
            if (panelImage == null) panelImage = _panel.AddComponent<Image>();
            panelImage.color = PanelColor;

            Text title = CreateText(_panel.transform, "Title", "SETTINGS & CONTROLS", 48, TextAnchor.MiddleCenter);
            title.rectTransform.anchoredPosition = new Vector2(0f, 460f);
            title.fontStyle = FontStyle.Bold;

            // --- Left Column: Audio, Accessibility, Fixed Reference ---
            float leftY = 360f;
            Text audioHeader = CreateText(_panel.transform, "AudioHeader", "AUDIO & ACCESSIBILITY", 24, TextAnchor.MiddleCenter);
            audioHeader.rectTransform.anchoredPosition = new Vector2(-300f, leftY);
            audioHeader.fontStyle = FontStyle.Bold;
            audioHeader.color = SectionHeaderColor;
            leftY -= 48f;

            _masterSlider = CreateLabelledSlider(_panel.transform, "Master Volume", -440f, -180f, ref leftY);
            _musicSlider = CreateLabelledSlider(_panel.transform, "Music Volume", -440f, -180f, ref leftY);
            _effectsSlider = CreateLabelledSlider(_panel.transform, "Effects Volume", -440f, -180f, ref leftY);
            _shakeSlider = CreateLabelledSlider(_panel.transform, "Screen Shake", -440f, -180f, ref leftY);
            _reducedFlashingToggle = CreateLabelledToggle(_panel.transform, "Reduced Flashing", -440f, -280f, ref leftY);
            _extendedTellToggle = CreateLabelledToggle(_panel.transform, "Longer Boss Tells", -440f, -280f, ref leftY);

            leftY -= 15f;
            Text fixedHeader = CreateText(_panel.transform, "FixedHeader", "COMBAT CONTROLS REFERENCE", 22, TextAnchor.MiddleCenter);
            fixedHeader.rectTransform.anchoredPosition = new Vector2(-300f, leftY);
            fixedHeader.fontStyle = FontStyle.Bold;
            fixedHeader.color = SectionHeaderColor;
            leftY -= 36f;

            Text fixedList = CreateText(
                _panel.transform, "FixedList",
                "Move: W, A, S, D  /  Left Stick\n" +
                "Light Attack: Left Mouse  /  Gamepad X\n" +
                "Heavy Attack: Shift + Left Mouse  /  Gamepad RT\n" +
                "Parry (Deflect): Right Mouse  /  Q  /  Gamepad RB\n" +
                "Jump: F  /  Left Alt  /  Gamepad A\n" +
                "Look / Orbit: Mouse Delta  /  Right Stick\n" +
                "Pause Menu: Escape  /  Gamepad Start",
                18, TextAnchor.MiddleLeft);
            fixedList.rectTransform.anchoredPosition = new Vector2(-300f, leftY - 40f);
            fixedList.rectTransform.sizeDelta = new Vector2(460f, 150f);
            fixedList.color = MutedText;

            // Wire audio & accessibility listeners
            _masterSlider.onValueChanged.AddListener(SetMasterVolume);
            _musicSlider.onValueChanged.AddListener(SetMusicVolume);
            _effectsSlider.onValueChanged.AddListener(SetEffectsVolume);
            _shakeSlider.onValueChanged.AddListener(SetShakeIntensity);
            _reducedFlashingToggle.onValueChanged.AddListener(SetReducedFlashing);
            _extendedTellToggle.onValueChanged.AddListener(SetExtendedTell);

            // --- Right Column: Rebindable Controls ---
            float rightY = 360f;
            Text controlsHeader = CreateText(_panel.transform, "ControlsHeader", "REBINDABLE CONTROLS", 24, TextAnchor.MiddleCenter);
            controlsHeader.rectTransform.anchoredPosition = new Vector2(300f, rightY);
            controlsHeader.fontStyle = FontStyle.Bold;
            controlsHeader.color = SectionHeaderColor;
            rightY -= 48f;

            CreateRebindRow(_panel.transform, "Light Attack", "LightAttack", this, 160f, 400f, ref rightY);
            CreateRebindRow(_panel.transform, "Guard / Deflect", "Guard", this, 160f, 400f, ref rightY);
            CreateRebindRow(_panel.transform, "Parry Strike", "Parry", this, 160f, 400f, ref rightY);
            CreateRebindRow(_panel.transform, "Dash / Dodge", "Dash", this, 160f, 400f, ref rightY);
            CreateRebindRow(_panel.transform, "Jump", "Jump", this, 160f, 400f, ref rightY);
            CreateRebindRow(_panel.transform, "Special Ability", "Special", this, 160f, 400f, ref rightY);
            CreateRebindRow(_panel.transform, "Heal Flask", "Heal", this, 160f, 400f, ref rightY);
            CreateRebindRow(_panel.transform, "Lock On Target", "LockOn", this, 160f, 400f, ref rightY);
            CreateRebindRow(_panel.transform, "Cycle Camera", "CycleCamera", this, 160f, 400f, ref rightY);

            // Bottom Buttons
            CreateButton(_panel.transform, "DevRulesBtn", "Developer Options & Rules", new Vector2(-190f, -460f), OpenDeveloperRules);
            CreateButton(_panel.transform, "ResetControls", "Reset Controls", new Vector2(140f, -460f), ResetControls);
            CreateButton(_panel.transform, "Back", "Back", new Vector2(440f, -460f), Close);

            // --- Developer Rules Panel ---
            _developerRulesPanel = BuildDeveloperRulesPanel(_panel.transform);
            _developerRulesPanel.SetActive(false);

            _rebindRows = _panel.GetComponentsInChildren<RebindButton>(true);
            _gameplayMap = _actions != null ? _actions.FindActionMap(GameplayMapName, throwIfNotFound: false) : null;
            RefreshRebindLabels();
            SyncControls();
        }

        private GameObject BuildDeveloperRulesPanel(Transform parent)
        {
            GameObject panel = CreateFullScreenPanel(parent, "DeveloperRulesPanel", DevPanelColor);

            Text title = CreateText(panel.transform, "DevTitle", "DEVELOPER OPTIONS & GAME RULES (READ-ONLY)", 38, TextAnchor.MiddleCenter);
            title.rectTransform.anchoredPosition = new Vector2(0f, 470f);
            title.fontStyle = FontStyle.Bold;
            title.color = Color.white;

            Text subtitle = CreateText(panel.transform, "DevSubtitle", "Live Game Balancing, Timing Windows, Mechanics & Adaptive Boss AI Rules", 20, TextAnchor.MiddleCenter);
            subtitle.rectTransform.anchoredPosition = new Vector2(0f, 430f);
            subtitle.color = SectionHeaderColor;

            // Column 1: Combat Mechanics & Timing Rules
            Text col1Header = CreateText(panel.transform, "Col1Header", "COMBAT MECHANICS & TIMING", 22, TextAnchor.MiddleCenter);
            col1Header.rectTransform.anchoredPosition = new Vector2(-540f, 380f);
            col1Header.fontStyle = FontStyle.Bold;
            col1Header.color = Accent;

            Text col1Body = CreateText(
                panel.transform, "Col1Body",
                "- PARRY WINDOW: 0.40s (~24 frames) on Right Click / Q.\n" +
                "  Committed parry turns blow aside, shatters 45 Posture points,\n" +
                "  inflicts hit-stop (0.14s) & world slow-motion (0.25s).\n\n" +
                "- DEFLECT WINDOW: 0.38s (~23 frames) on timed Guard.\n" +
                "  Negates all damage, deals 28 Posture damage, generates Focus.\n\n" +
                "- BLOCK: Late hold absorbs hit with 15% chip damage & Posture loss.\n\n" +
                "- POSTURE & RIPOSTE: Draining 100 boss Posture causes guard break.\n" +
                "  Boss reels and kneels for 2.8s; strike within 2.8m for high-damage\n" +
                "  Riposte / Execution (72 dmg, unparryable).\n\n" +
                "- PERILOUS ATTACKS: Red Kanji / Telegraph = UNBLOCKABLE.\n" +
                "  Completely bypasses Guard and Parry. Must Dodge or Jump!\n\n" +
                "- JUMP MECHANICS: 7.5 m/s launch upward with aerial steering.\n" +
                "  Physically clears low sweeps, ground slams, expanding shockwaves\n" +
                "  and floor fire hazard fields.\n\n" +
                "- RANGED & SHOCKWAVES: Standard shockwaves parryable with Right\n" +
                "  Click or cleared by Jump/Dodge. Cataclysmic wave 8.5m radius.\n\n" +
                "- FLOOR HAZARDS: 3 dmg/tick, ignores i-frames. Jump/exit fire.",
                16, TextAnchor.UpperLeft);
            col1Body.rectTransform.anchoredPosition = new Vector2(-540f, 130f);
            col1Body.rectTransform.sizeDelta = new Vector2(490f, 460f);
            col1Body.color = MutedText;

            // Column 2: AI Adaptation & Boss Rules
            Text col2Header = CreateText(panel.transform, "Col2Header", "AI ADAPTATION & BEHAVIOR RULES", 22, TextAnchor.MiddleCenter);
            col2Header.rectTransform.anchoredPosition = new Vector2(0f, 380f);
            col2Header.fontStyle = FontStyle.Bold;
            col2Header.color = Accent;

            Text col2Body = CreateText(
                panel.transform, "Col2Body",
                "- CONTINUOUS HABIT TRACKING:\n" +
                "  * Dodge Bias (Direction & Timing)\n" +
                "  * Attack Cadence & Combo Rhythm\n" +
                "  * Distance & Spacing (Kiting vs Melee Pressure)\n" +
                "  * Healing Window Timing (Punish Window)\n" +
                "  * Defense Preference (Deflect vs Block vs Dodge)\n\n" +
                "- ADAPTATION THRESHOLD (0.65 Confidence):\n" +
                "  * Roll Spammers -> Boss deploys Wide Sweeps (4.0m, 120° arc)\n" +
                "  * Aggressive Attackers -> Boss counters with Hyper-Armour Jabs\n" +
                "    (0.28s startup) & reactive Parry Stances\n" +
                "  * Kiters / Runners -> Boss unleashes 16 m/s Charge (Knockdown)\n" +
                "  * Guard Turtles -> Boss unleashes Perilous Overhead (Unblockable)\n\n" +
                "- PHASE 2 ENRAGE (< 50% Boss HP):\n" +
                "  * Attack recovery & cooldowns reduced by 20%\n" +
                "  * Cataclysmic Wave unlocked (8.5m arena-wide perilous wave)\n" +
                "  * Resolve gambits trigger on posture threats.",
                16, TextAnchor.UpperLeft);
            col2Body.rectTransform.anchoredPosition = new Vector2(0f, 130f);
            col2Body.rectTransform.sizeDelta = new Vector2(490f, 460f);
            col2Body.color = MutedText;

            // Column 3: Attack Catalog & Reach
            Text col3Header = CreateText(panel.transform, "Col3Header", "ATTACK CATALOG & RANGES", 22, TextAnchor.MiddleCenter);
            col3Header.rectTransform.anchoredPosition = new Vector2(540f, 380f);
            col3Header.fontStyle = FontStyle.Bold;
            col3Header.color = Accent;

            Text col3Body = CreateText(
                panel.transform, "Col3Body",
                "- PLAYER WEAPONS:\n" +
                "  * Balanced Blade:\n" +
                "    - Light 1-3: Range 2.1m - 2.4m, 110°-130° Arc\n" +
                "    - Heavy Overhead: Range 2.8m, 42 dmg (3.5 m/s lunge)\n" +
                "    - Whirlwind Special: Range 3.4m, 360° Sphere\n" +
                "  * Greatsword (Hyper-Armour):\n" +
                "    - Light 1-2: Range 2.7m - 2.9m, 140°-150° Arc\n" +
                "    - Earthbreaker: Range 3.0m, 56 dmg, Heavy Poise\n" +
                "    - Seismic Special: Range 3.8m, 360° Sphere\n" +
                "  * Energy Blade (Rapid Flurry):\n" +
                "    - Light 1-4: Range 1.9m - 2.1m fast pokes\n" +
                "    - Lunge Thrust: Range 3.4m Box, 30 dmg (8 m/s lunge)\n" +
                "    - Blade Dance: Range 2.8m, 360° Sphere\n\n" +
                "- BOSS MOVESET (7 Attacks):\n" +
                "  * Jab: 3.0m reach (0.28s startup, 8 dmg)\n" +
                "  * Wide Sweep: 4.0m reach, 120° arc (0.45s startup, 12 dmg)\n" +
                "  * Ground Slam: 3.6m reach 360° + 3m fire hazard (8 dmg)\n" +
                "  * Charge: 16 m/s lunge, ~12m effective reach (15 dmg)\n" +
                "  * Perilous Overhead: 3.4m reach, Unblockable! (26 dmg)\n" +
                "  * Shockwave: 7.0m projectile wave + 3.5m hazard (14 dmg)\n" +
                "  * Cataclysmic Wave: 8.5m arena wave, Unblockable! (22 dmg)",
                16, TextAnchor.UpperLeft);
            col3Body.rectTransform.anchoredPosition = new Vector2(540f, 130f);
            col3Body.rectTransform.sizeDelta = new Vector2(490f, 460f);
            col3Body.color = MutedText;

            // Dev Panel Buttons
            CreateButton(panel.transform, "ToggleOverlayBtn", "Toggle AI Telemetry Overlay (F1)", new Vector2(-190f, -460f), ToggleAiDebugOverlay);
            CreateButton(panel.transform, "CloseDevRulesBtn", "Back to Settings", new Vector2(190f, -460f), CloseDeveloperRules);

            return panel;
        }

        private static Slider CreateLabelledSlider(Transform parent, string caption, float labelX, float controlX, ref float y)
        {
            CreateRowLabel(parent, caption, labelX, y);
            Slider slider = CreateSlider(parent, caption, new Vector2(controlX, y), Accent, width: 220f);
            y -= 48f;
            return slider;
        }

        private static Toggle CreateLabelledToggle(Transform parent, string caption, float labelX, float controlX, ref float y)
        {
            CreateRowLabel(parent, caption, labelX, y);
            Toggle toggle = CreateToggle(parent, caption, new Vector2(controlX, y), ToggleAccent);
            y -= 48f;
            return toggle;
        }

        private static void CreateRowLabel(Transform parent, string caption, float x, float y)
        {
            Text label = CreateText(parent, $"{caption} Label", caption, 20, TextAnchor.MiddleRight);
            label.rectTransform.anchoredPosition = new Vector2(x, y);
            label.rectTransform.sizeDelta = new Vector2(240f, 32f);
        }

        private static void CreateRebindRow(
            Transform parent, string caption, string actionName, SettingsMenu menu, float labelX, float controlX, ref float y)
        {
            Text label = CreateText(parent, $"{caption} Label", caption, 20, TextAnchor.MiddleRight);
            label.rectTransform.anchoredPosition = new Vector2(labelX, y);
            label.rectTransform.sizeDelta = new Vector2(220f, 32f);

            var buttonObject = new GameObject($"{caption} Rebind", typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(controlX, y);
            rect.sizeDelta = new Vector2(190f, 30f);

            buttonObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);

            Text key = CreateText(buttonObject.transform, "Key", "—", 20, TextAnchor.MiddleCenter);
            key.rectTransform.sizeDelta = new Vector2(190f, 30f);

            var rebind = buttonObject.AddComponent<RebindButton>();
            rebind.Configure(actionName, 0, key, menu);

            buttonObject.GetComponent<Button>().onClick.AddListener(rebind.OnClick);

            y -= 38f;
        }

        private static Text CreateText(
            Transform parent,
            string name,
            string content,
            int fontSize,
            TextAnchor alignment)
        {
            var textObject = new GameObject(name, typeof(Text));
            textObject.transform.SetParent(parent, false);

            var text = textObject.GetComponent<Text>();
            text.text = content;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1000f, 80f);

            return text;
        }

        private static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Vector2 anchoredPosition,
            UnityAction onClick)
        {
            var buttonObject = new GameObject(name, typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(320f, 56f);

            buttonObject.GetComponent<Image>().color = Color.white;
            buttonObject.AddComponent<Outline>().effectColor = new Color(0.62f, 0.52f, 0.4f, 0.55f);

            Text text = CreateText(buttonObject.transform, "Label", label, 24, TextAnchor.MiddleCenter);
            text.rectTransform.sizeDelta = new Vector2(320f, 56f);
            text.color = new Color(0.86f, 0.8f, 0.7f);

            var button = buttonObject.GetComponent<Button>();
            button.colors = new ColorBlock
            {
                normalColor = new Color(0.07f, 0.05f, 0.05f, 0.82f),
                highlightedColor = new Color(0.32f, 0.1f, 0.06f, 0.9f),
                pressedColor = new Color(0.5f, 0.14f, 0.07f, 1f),
                selectedColor = new Color(0.24f, 0.08f, 0.05f, 0.9f),
                disabledColor = new Color(0.05f, 0.05f, 0.05f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = 0.12f
            };
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            return button;
        }

        private static Slider CreateSlider(
            Transform parent, string name, Vector2 anchoredPosition, Color accent, float width = 340f)
        {
            GameObject sliderObject = DefaultControls.CreateSlider(new DefaultControls.Resources());
            sliderObject.name = name;
            sliderObject.transform.SetParent(parent, false);

            var rect = (RectTransform)sliderObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(width, 22f);

            var slider = sliderObject.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;

            Recolour(sliderObject.transform, "Fill Area/Fill", accent);
            Recolour(sliderObject.transform, "Background", new Color(1f, 1f, 1f, 0.16f));
            Recolour(sliderObject.transform, "Handle Slide Area/Handle", Color.white);

            return slider;
        }

        private static Toggle CreateToggle(
            Transform parent, string name, Vector2 anchoredPosition, Color accent)
        {
            GameObject toggleObject = DefaultControls.CreateToggle(new DefaultControls.Resources());
            toggleObject.name = name;
            toggleObject.transform.SetParent(parent, false);

            var rect = (RectTransform)toggleObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(30f, 30f);

            Transform label = toggleObject.transform.Find("Label");
            if (label != null)
            {
                label.gameObject.SetActive(false);
            }

            Recolour(toggleObject.transform, "Background", new Color(1f, 1f, 1f, 0.2f));
            Recolour(toggleObject.transform, "Background/Checkmark", accent);

            return toggleObject.GetComponent<Toggle>();
        }

        private static void Recolour(Transform root, string path, Color color)
        {
            Transform target = root.Find(path);
            var image = target != null ? target.GetComponent<Image>() : null;
            if (image != null)
            {
                image.color = color;
            }
        }

        private static GameObject CreateFullScreenPanel(Transform parent, string name, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            panel.GetComponent<Image>().color = color;
            return panel;
        }

        /// <summary>
        /// Moves the panel above its siblings so nothing draws through it.
        /// </summary>
        /// <remarks>
        /// Called both at build time, so the order is already correct in the saved scene, and every
        /// time the panel opens, so it stays correct however the surrounding screens were assembled.
        /// </remarks>
        public void RaiseToTop()
        {
            if (_panel != null)
            {
                _panel.transform.SetAsLastSibling();
            }
        }

        /// <summary>Hides the settings panel.</summary>
        public void Close()
        {
            if (_activeRebind != null)
            {
                _activeRebind.Cancel();
            }

            if (_rebindCoroutine != null)
            {
                StopCoroutine(_rebindCoroutine);
                _rebindCoroutine = null;
            }

            CloseDeveloperRules();

            if (_panel != null)
            {
                _panel.SetActive(false);
            }
        }

        /// <summary>Opens the read-only developer options and rules viewer.</summary>
        public void OpenDeveloperRules()
        {
            if (_developerRulesPanel != null)
            {
                _developerRulesPanel.SetActive(true);
                _developerRulesPanel.transform.SetAsLastSibling();
            }
        }

        /// <summary>Closes the read-only developer options and rules viewer.</summary>
        public void CloseDeveloperRules()
        {
            if (_developerRulesPanel != null)
            {
                _developerRulesPanel.SetActive(false);
            }
        }

        /// <summary>Toggles the live AI telemetry and adaptation debug overlay.</summary>
        public void ToggleAiDebugOverlay()
        {
            var monoBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
            for (int i = 0; i < monoBehaviours.Length; i++)
            {
                if (monoBehaviours[i] != null && monoBehaviours[i].GetType().Name == "AdaptationDebugOverlay")
                {
                    monoBehaviours[i].SendMessage("Toggle", SendMessageOptions.DontRequireReceiver);
                    break;
                }
            }
        }

        // --- Control callbacks, wired to the sliders and toggles by the interface generator. ---

        /// <summary>Sets the master volume from its slider.</summary>
        /// <param name="value">Linear volume, zero to one.</param>
        public void SetMasterVolume(float value)
        {
            if (_isSyncing) return;
            _settings.MasterVolume = value;
            ApplyVolumes();
            Save();
        }

        /// <summary>Sets the music volume from its slider.</summary>
        /// <param name="value">Linear volume, zero to one.</param>
        public void SetMusicVolume(float value)
        {
            if (_isSyncing) return;
            _settings.MusicVolume = value;
            ApplyVolumes();
            Save();
        }

        /// <summary>Sets the effects volume from its slider.</summary>
        /// <param name="value">Linear volume, zero to one.</param>
        public void SetEffectsVolume(float value)
        {
            if (_isSyncing) return;
            _settings.EffectsVolume = value;
            ApplyVolumes();
            Save();
        }

        /// <summary>Sets the screen-shake intensity from its slider.</summary>
        /// <param name="value">Intensity, zero (off) to one.</param>
        public void SetShakeIntensity(float value)
        {
            if (_isSyncing) return;
            _settings.ScreenShakeIntensity = value;
            ApplyShake();
            Save();
        }

        /// <summary>Toggles reduced flashing.</summary>
        /// <param name="on">Whether flashes are suppressed.</param>
        public void SetReducedFlashing(bool on)
        {
            if (_isSyncing) return;
            _settings.ReducedFlashing = on;
            ApplyFlashing();
            Save();
        }

        /// <summary>Toggles extended adaptation-tell duration.</summary>
        /// <param name="on">Whether tells linger longer.</param>
        public void SetExtendedTell(bool on)
        {
            if (_isSyncing) return;
            _settings.ExtendedTellDuration = on;
            ApplyTell();
            Save();
        }

        // --- Control rebinding. ---

        /// <summary>
        /// Starts listening for a new key or mouse button to bind to a control row.
        /// </summary>
        /// <remarks>
        /// The action is disabled for the duration so the press being captured does not also fire the
        /// control it is rebinding. Escape cancels, and mouse motion/pointer positions are excluded so
        /// moving the mouse cannot be mistaken for a new binding, while left, right and middle mouse
        /// buttons are fully supported. On completion the override is serialised straight into the
        /// settings, so it survives the session exactly as the sliders do.
        /// </remarks>
        /// <param name="row">The row whose control is being rebound.</param>
        public void BeginRebind(RebindButton row)
        {
            if (row == null || _gameplayMap == null || _activeRebind != null || _rebindCoroutine != null)
            {
                return;
            }

            InputAction action = _gameplayMap.FindAction(row.ActionName, throwIfNotFound: false);

            if (action == null)
            {
                return;
            }

            _rebindCoroutine = StartCoroutine(RebindRoutine(row, action));
        }

        private System.Collections.IEnumerator RebindRoutine(RebindButton row, InputAction action)
        {
            row.SetLabel("Press key or click…");

            // Wait a frame and ensure any mouse button from the UI button click has been released
            yield return null;
            while (Mouse.current != null && (Mouse.current.leftButton.isPressed || Mouse.current.rightButton.isPressed || Mouse.current.middleButton.isPressed))
            {
                yield return null;
            }

            bool wasEnabled = action.enabled;
            action.Disable();

            _activeRebind = action.PerformInteractiveRebinding(row.BindingIndex)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithControlsExcluding("<Pointer>/position")
                .WithControlsExcluding("<Pointer>/delta")
                .WithControlsExcluding("<Pointer>/scroll")
                .WithExpectedControlType("Button")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(op => CompleteRebind(op, action, row, wasEnabled, save: true))
                .OnCancel(op => CompleteRebind(op, action, row, wasEnabled, save: false))
                .Start();
        }

        /// <summary>Clears every rebinding back to the shipped defaults.</summary>
        public void ResetControls()
        {
            if (_actions == null)
            {
                return;
            }

            _actions.RemoveAllBindingOverrides();
            _settings.InputRebinds = string.Empty;
            Save();
            RefreshRebindLabels();
        }

        /// <summary>Finishes a rebind, re-enabling the action and recording the result.</summary>
        private void CompleteRebind(
            InputActionRebindingExtensions.RebindingOperation operation,
            InputAction action,
            RebindButton row,
            bool wasEnabled,
            bool save)
        {
            operation.Dispose();
            _activeRebind = null;
            _rebindCoroutine = null;

            if (wasEnabled)
            {
                action.Enable();
            }

            RefreshLabel(row, action);

            if (save)
            {
                _settings.InputRebinds = _actions.SaveBindingOverridesAsJson();
                Save();
            }
        }

        /// <summary>Rewrites every rebind row's label from its action's current binding.</summary>
        private void RefreshRebindLabels()
        {
            if (_rebindRows == null || _gameplayMap == null)
            {
                return;
            }

            foreach (RebindButton row in _rebindRows)
            {
                InputAction action = _gameplayMap.FindAction(row.ActionName, throwIfNotFound: false);

                if (action != null)
                {
                    RefreshLabel(row, action);
                }
            }
        }

        /// <summary>Writes one row's label from its binding, stripped of the device prefix.</summary>
        private static void RefreshLabel(RebindButton row, InputAction action)
        {
            if (row.BindingIndex < 0 || row.BindingIndex >= action.bindings.Count)
            {
                return;
            }

            string text = InputControlPath.ToHumanReadableString(
                action.bindings[row.BindingIndex].effectivePath,
                InputControlPath.HumanReadableStringOptions.OmitDevice);

            if (string.Equals(text, "leftButton", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Left Button", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "LMB", System.StringComparison.OrdinalIgnoreCase))
            {
                text = "Left Click";
            }
            else if (string.Equals(text, "rightButton", System.StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(text, "Right Button", System.StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(text, "RMB", System.StringComparison.OrdinalIgnoreCase))
            {
                text = "Right Click";
            }
            else if (string.Equals(text, "middleButton", System.StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(text, "Middle Button", System.StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(text, "MMB", System.StringComparison.OrdinalIgnoreCase))
            {
                text = "Middle Click";
            }
            else if (string.IsNullOrEmpty(text))
            {
                text = "—";
            }

            row.SetLabel(text);
        }

        /// <summary>Assigns the references. Used by the interface generator.</summary>
        /// <param name="panel">Root object of the panel.</param>
        /// <param name="master">Master volume slider.</param>
        /// <param name="music">Music volume slider.</param>
        /// <param name="effects">Effects volume slider.</param>
        /// <param name="shake">Screen-shake slider.</param>
        /// <param name="reducedFlashing">Reduced-flashing toggle.</param>
        /// <param name="extendedTell">Extended-tell toggle.</param>
        /// <param name="actions">Shared input actions asset.</param>
        /// <param name="developerRulesPanel">Read-only developer rules panel.</param>
        public void Bind(
            GameObject panel,
            Slider master,
            Slider music,
            Slider effects,
            Slider shake,
            Toggle reducedFlashing,
            Toggle extendedTell,
            InputActionAsset actions,
            GameObject developerRulesPanel = null)
        {
            _panel = panel;
            _masterSlider = master;
            _musicSlider = music;
            _effectsSlider = effects;
            _shakeSlider = shake;
            _reducedFlashingToggle = reducedFlashing;
            _extendedTellToggle = extendedTell;
            _actions = actions;
            _developerRulesPanel = developerRulesPanel;
        }

        private SettingsData Load()
        {
            if (_saveService == null && ServiceRegistry.Current != null)
            {
                ServiceRegistry.Current.TryGet(out _saveService);
            }

            return (_saveService != null && _saveService.TryLoad(SaveKeys.Settings, out SettingsData loaded))
                ? loaded
                : new SettingsData();
        }

        private void Save() => _saveService?.Save(SaveKeys.Settings, _settings);

        /// <summary>Applies every setting to the systems that honour it.</summary>
        private void ApplyAll()
        {
            ApplyVolumes();
            ApplyShake();
            ApplyFlashing();
            ApplyTell();
        }

        private void ApplyVolumes()
        {
            if (ServiceRegistry.Current.TryGet(out IAudioService audio))
            {
                audio.SetBusVolume(AudioBus.Master, _settings.MasterVolume);
                audio.SetBusVolume(AudioBus.Music, _settings.MusicVolume);
                audio.SetBusVolume(AudioBus.Effects, _settings.EffectsVolume);
            }
        }

        private void ApplyShake()
        {
            if (ServiceRegistry.Current.TryGet(out IScreenShake shake))
            {
                shake.SetUserIntensity(_settings.ScreenShakeIntensity);
            }
        }

        private void ApplyFlashing() => FeedbackSettings.ReducedFlashing = _settings.ReducedFlashing;

        private void ApplyTell() =>
            FeedbackSettings.TellHoldMultiplier = _settings.ExtendedTellDuration ? ExtendedTellMultiplier : 1f;

        /// <summary>Loads any saved control rebindings onto the shared input asset.</summary>
        private void ApplyInputOverrides()
        {
            if (_actions != null && !string.IsNullOrEmpty(_settings.InputRebinds))
            {
                _actions.LoadBindingOverridesFromJson(_settings.InputRebinds);
            }
        }

        /// <summary>Writes the loaded values into the controls without treating it as an edit.</summary>
        private void SyncControls()
        {
            if (_settings == null)
            {
                _settings = Load();
            }
            if (_settings == null)
            {
                _settings = new SettingsData();
            }

            _isSyncing = true;

            if (_masterSlider != null) _masterSlider.value = _settings.MasterVolume;
            if (_musicSlider != null) _musicSlider.value = _settings.MusicVolume;
            if (_effectsSlider != null) _effectsSlider.value = _settings.EffectsVolume;
            if (_shakeSlider != null) _shakeSlider.value = _settings.ScreenShakeIntensity;
            if (_reducedFlashingToggle != null) _reducedFlashingToggle.isOn = _settings.ReducedFlashing;
            if (_extendedTellToggle != null) _extendedTellToggle.isOn = _settings.ExtendedTellDuration;

            _isSyncing = false;
        }
    }
}
