using AdaptiveBossArena.UI;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// Builds the settings panel, all control rebinding options, and the read-only developer rules viewer.
    /// </summary>
    internal static class SettingsMenuBuilder
    {
        private static readonly Color PanelColor = new Color(0.03f, 0.03f, 0.05f, 0.985f);
        private static readonly Color DevPanelColor = new Color(0.02f, 0.02f, 0.04f, 0.995f);
        private static readonly Color Accent = new Color(0.45f, 0.85f, 0.95f);
        private static readonly Color ToggleAccent = new Color(0.55f, 0.9f, 0.6f);
        private static readonly Color MutedText = new Color(0.72f, 0.76f, 0.82f);
        private static readonly Color SectionHeaderColor = new Color(0.95f, 0.75f, 0.45f);

        /// <summary>Builds the settings menu, all controls, and developer rules panel.</summary>
        public static SettingsMenu Build(Transform root, InputActionAsset actions)
        {
            GameObject panel = CreateFullScreenPanel(root, "SettingsPanel");

            Text title = UiBuilder.CreateText(panel.transform, "Title", "SETTINGS & CONTROLS", 48, TextAnchor.MiddleCenter);
            title.rectTransform.anchoredPosition = new Vector2(0f, 460f);
            title.fontStyle = FontStyle.Bold;

            // --- Left Column: Audio, Accessibility, Fixed Reference ---
            float leftY = 360f;
            Text audioHeader = UiBuilder.CreateText(panel.transform, "AudioHeader", "AUDIO & ACCESSIBILITY", 24, TextAnchor.MiddleCenter);
            audioHeader.rectTransform.anchoredPosition = new Vector2(-300f, leftY);
            audioHeader.fontStyle = FontStyle.Bold;
            audioHeader.color = SectionHeaderColor;
            leftY -= 48f;

            Slider master = LabelledSlider(panel.transform, "Master Volume", -440f, -180f, ref leftY);
            Slider music = LabelledSlider(panel.transform, "Music Volume", -440f, -180f, ref leftY);
            Slider effects = LabelledSlider(panel.transform, "Effects Volume", -440f, -180f, ref leftY);
            Slider shake = LabelledSlider(panel.transform, "Screen Shake", -440f, -180f, ref leftY);
            Toggle reducedFlashing = LabelledToggle(panel.transform, "Reduced Flashing", -440f, -280f, ref leftY);
            Toggle extendedTell = LabelledToggle(panel.transform, "Longer Boss Tells", -440f, -280f, ref leftY);

            leftY -= 15f;
            Text fixedHeader = UiBuilder.CreateText(panel.transform, "FixedHeader", "COMBAT CONTROLS REFERENCE", 22, TextAnchor.MiddleCenter);
            fixedHeader.rectTransform.anchoredPosition = new Vector2(-300f, leftY);
            fixedHeader.fontStyle = FontStyle.Bold;
            fixedHeader.color = SectionHeaderColor;
            leftY -= 36f;

            Text fixedList = UiBuilder.CreateText(
                panel.transform, "FixedList",
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

            // --- Right Column: Rebindable Controls ---
            float rightY = 360f;
            Text controlsHeader = UiBuilder.CreateText(panel.transform, "ControlsHeader", "REBINDABLE CONTROLS", 24, TextAnchor.MiddleCenter);
            controlsHeader.rectTransform.anchoredPosition = new Vector2(300f, rightY);
            controlsHeader.fontStyle = FontStyle.Bold;
            controlsHeader.color = SectionHeaderColor;
            rightY -= 48f;

            var menu = root.gameObject.AddComponent<SettingsMenu>();

            RebindRow(panel.transform, "Dash / Dodge", "Dash", menu, 160f, 400f, ref rightY);
            RebindRow(panel.transform, "Jump", "Jump", menu, 160f, 400f, ref rightY);
            RebindRow(panel.transform, "Parry Strike", "Parry", menu, 160f, 400f, ref rightY);
            RebindRow(panel.transform, "Guard / Deflect", "Guard", menu, 160f, 400f, ref rightY);
            RebindRow(panel.transform, "Special Ability", "Special", menu, 160f, 400f, ref rightY);
            RebindRow(panel.transform, "Heal Flask", "Heal", menu, 160f, 400f, ref rightY);
            RebindRow(panel.transform, "Lock On Target", "LockOn", menu, 160f, 400f, ref rightY);
            RebindRow(panel.transform, "Cycle Camera", "CycleCamera", menu, 160f, 400f, ref rightY);

            // Wire audio & accessibility
            WireSlider(master, menu.SetMasterVolume);
            WireSlider(music, menu.SetMusicVolume);
            WireSlider(effects, menu.SetEffectsVolume);
            WireSlider(shake, menu.SetShakeIntensity);
            WireToggle(reducedFlashing, menu.SetReducedFlashing);
            WireToggle(extendedTell, menu.SetExtendedTell);

            // --- Developer Rules & Options Panel ---
            GameObject devPanel = BuildDeveloperRulesPanel(panel.transform, menu);
            devPanel.SetActive(false);

            menu.Bind(panel, master, music, effects, shake, reducedFlashing, extendedTell, actions, devPanel);

            // Bottom Buttons
            UiBuilder.CreateButton(panel.transform, "DevRulesBtn", "Developer Options & Rules", new Vector2(-190f, -460f), menu.OpenDeveloperRules);
            UiBuilder.CreateButton(panel.transform, "ResetControls", "Reset Controls", new Vector2(140f, -460f), menu.ResetControls);
            UiBuilder.CreateButton(panel.transform, "Back", "Back", new Vector2(440f, -460f), menu.Close);

            panel.SetActive(false);
            return menu;
        }

        private static GameObject BuildDeveloperRulesPanel(Transform parent, SettingsMenu menu)
        {
            GameObject panel = CreateFullScreenPanel(parent, "DeveloperRulesPanel");
            panel.GetComponent<Image>().color = DevPanelColor;

            Text title = UiBuilder.CreateText(panel.transform, "DevTitle", "DEVELOPER OPTIONS & GAME RULES (READ-ONLY)", 38, TextAnchor.MiddleCenter);
            title.rectTransform.anchoredPosition = new Vector2(0f, 470f);
            title.fontStyle = FontStyle.Bold;
            title.color = Color.white;

            Text subtitle = UiBuilder.CreateText(panel.transform, "DevSubtitle", "Live Game Balancing, Timing Windows, Mechanics & Adaptive Boss AI Rules", 20, TextAnchor.MiddleCenter);
            subtitle.rectTransform.anchoredPosition = new Vector2(0f, 430f);
            subtitle.color = SectionHeaderColor;

            // Three Columns
            // Column 1: Combat Mechanics & Timing Rules
            Text col1Header = UiBuilder.CreateText(panel.transform, "Col1Header", "COMBAT MECHANICS & TIMING", 22, TextAnchor.MiddleCenter);
            col1Header.rectTransform.anchoredPosition = new Vector2(-540f, 380f);
            col1Header.fontStyle = FontStyle.Bold;
            col1Header.color = Accent;

            Text col1Body = UiBuilder.CreateText(
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
            Text col2Header = UiBuilder.CreateText(panel.transform, "Col2Header", "AI ADAPTATION & BEHAVIOR RULES", 22, TextAnchor.MiddleCenter);
            col2Header.rectTransform.anchoredPosition = new Vector2(0f, 380f);
            col2Header.fontStyle = FontStyle.Bold;
            col2Header.color = Accent;

            Text col2Body = UiBuilder.CreateText(
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
            Text col3Header = UiBuilder.CreateText(panel.transform, "Col3Header", "ATTACK CATALOG & RANGES", 22, TextAnchor.MiddleCenter);
            col3Header.rectTransform.anchoredPosition = new Vector2(540f, 380f);
            col3Header.fontStyle = FontStyle.Bold;
            col3Header.color = Accent;

            Text col3Body = UiBuilder.CreateText(
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
            UiBuilder.CreateButton(panel.transform, "ToggleOverlayBtn", "Toggle AI Telemetry Overlay (F1)", new Vector2(-190f, -460f), menu.ToggleAiDebugOverlay);
            UiBuilder.CreateButton(panel.transform, "CloseDevRulesBtn", "Back to Settings", new Vector2(190f, -460f), menu.CloseDeveloperRules);

            return panel;
        }

        private static Slider LabelledSlider(Transform parent, string caption, float labelX, float controlX, ref float y)
        {
            RowLabel(parent, caption, labelX, y);
            Slider slider = UiBuilder.CreateSlider(parent, caption, new Vector2(controlX, y), Accent, width: 220f);
            y -= 48f;
            return slider;
        }

        private static Toggle LabelledToggle(Transform parent, string caption, float labelX, float controlX, ref float y)
        {
            RowLabel(parent, caption, labelX, y);
            Toggle toggle = UiBuilder.CreateToggle(parent, caption, new Vector2(controlX, y), ToggleAccent);
            y -= 48f;
            return toggle;
        }

        private static void RowLabel(Transform parent, string caption, float x, float y)
        {
            Text label = UiBuilder.CreateText(parent, $"{caption} Label", caption, 20, TextAnchor.MiddleRight);
            label.rectTransform.anchoredPosition = new Vector2(x, y);
            label.rectTransform.sizeDelta = new Vector2(240f, 32f);
        }

        private static void RebindRow(
            Transform parent, string caption, string actionName, SettingsMenu menu, float labelX, float controlX, ref float y)
        {
            Text label = UiBuilder.CreateText(parent, $"{caption} Label", caption, 20, TextAnchor.MiddleRight);
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

            Text key = UiBuilder.CreateText(buttonObject.transform, "Key", "—", 20, TextAnchor.MiddleCenter);
            key.rectTransform.sizeDelta = new Vector2(190f, 30f);

            var rebind = buttonObject.AddComponent<RebindButton>();
            rebind.Configure(actionName, 0, key, menu);

            UnityEventTools.AddPersistentListener(buttonObject.GetComponent<Button>().onClick, rebind.OnClick);

            y -= 38f;
        }

        private static GameObject CreateFullScreenPanel(Transform parent, string name)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            panel.GetComponent<Image>().color = PanelColor;
            return panel;
        }

        private static void WireSlider(Slider slider, UnityEngine.Events.UnityAction<float> onChanged) =>
            UnityEventTools.AddPersistentListener(slider.onValueChanged, onChanged);

        private static void WireToggle(Toggle toggle, UnityEngine.Events.UnityAction<bool> onChanged) =>
            UnityEventTools.AddPersistentListener(toggle.onValueChanged, onChanged);
    }
}
