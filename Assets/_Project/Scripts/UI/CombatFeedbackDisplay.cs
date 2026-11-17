using AdaptiveBossArena.AI;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Services;
using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveBossArena.UI
{
    /// <summary>Brief, colour-coded action callouts for both the player and the boss.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatFeedbackDisplay : MonoBehaviour
    {
        private const float HoldSeconds = 0.65f;
        private const float BossHoldSeconds = 0.9f;
        private const float FadeSeconds = 0.24f;
        [SerializeField] private Text _label;
        [SerializeField] private CanvasGroup _group;
        private Text _bossLabel;
        private CanvasGroup _bossGroup;
        private ICombatEventBus _events;
        private BossController _boss;
        private AttackDefinition _shownBossAttack;
        private float _remaining;
        private float _bossRemaining;

        public void Bind(Text label, CanvasGroup group) { _label = label; _group = group; }

        private void Start()
        {
            if (_label == null) _label = GetComponentInChildren<Text>(true);
            if (_group == null) _group = GetComponent<CanvasGroup>();
            BuildBossCallout();
            _boss = FindAnyObjectByType<BossController>();
            ServiceRegistry.Current.TryGet(out _events);
            if (_events != null) _events.EventRecorded += OnCombatEvent;
            if (_group != null) _group.alpha = 0f;
            if (_bossGroup != null) _bossGroup.alpha = 0f;
        }

        private void OnDestroy()
        {
            if (_events != null) _events.EventRecorded -= OnCombatEvent;
        }

        private void Update()
        {
            Fade(_group, ref _remaining);
            Fade(_bossGroup, ref _bossRemaining);

            if (_boss == null) _boss = FindAnyObjectByType<BossController>();
            if (_boss == null) return;

            // The event is the normal path. Polling the active attack is a safety net for a HUD
            // enabled after the attack-start event during a scene transition.
            AttackDefinition activeAttack = _boss.CurrentAttack;
            if (activeAttack == null)
            {
                _shownBossAttack = null;
            }
            else if (activeAttack != _shownBossAttack)
            {
                ShowBossAttack(activeAttack);
            }
        }

        private void OnCombatEvent(CombatEvent combatEvent)
        {
            if (combatEvent.Actor == CombatantTeam.Player)
            {
                switch (combatEvent.Kind)
                {
                    case CombatEventKind.GuardRaised: Show("GUARD • DEFLECT WINDOW", new Color(0.74f, 0.8f, 0.92f)); break;
                    case CombatEventKind.DodgePerformed: Show("DODGE", new Color(0.72f, 0.78f, 0.9f)); break;
                    case CombatEventKind.PerfectDodge: Show("PERFECT DODGE", new Color(0.42f, 0.92f, 1f)); break;
                    case CombatEventKind.Deflected: Show("DEFLECT", new Color(0.65f, 0.9f, 1f)); break;
                    case CombatEventKind.PlayerParryStarted: Show("PARRY STRIKE", new Color(1f, 0.78f, 0.3f)); break;
                    case CombatEventKind.PlayerParried: Show("PARRY!", new Color(1f, 0.55f, 0.22f)); break;
                    case CombatEventKind.Blocked: Show("BLOCKED", new Color(0.78f, 0.66f, 0.48f)); break;
                }
                return;
            }

            if (combatEvent.Actor != CombatantTeam.Boss) return;

            switch (combatEvent.Kind)
            {
                case CombatEventKind.AttackStarted:
                    if (_boss == null) _boss = FindAnyObjectByType<BossController>();
                    AttackDefinition attack = _boss != null ? _boss.CurrentAttack : null;
                    if (attack != null) ShowBossAttack(attack);
                    else ShowBoss("BOSS • ATTACKING", new Color(1f, 0.58f, 0.3f));
                    break;
                case CombatEventKind.GuardRaised:
                    ShowBoss("BOSS • PARRY STANCE", new Color(1f, 0.82f, 0.28f));
                    break;
                case CombatEventKind.Parried:
                    ShowBoss("BOSS • PARRIED YOU", new Color(1f, 0.42f, 0.22f));
                    break;
                case CombatEventKind.AttackLanded:
                    ShowBoss("BOSS • HIT CONFIRMED", new Color(1f, 0.32f, 0.22f));
                    break;
                case CombatEventKind.AttackEvaded:
                    ShowBoss("BOSS • ATTACK DODGED", new Color(0.88f, 0.62f, 0.38f));
                    break;
                case CombatEventKind.AttackWhiffed:
                    ShowBoss("BOSS • ATTACK MISSED", new Color(0.76f, 0.58f, 0.46f));
                    break;
                case CombatEventKind.PoiseBroken:
                    ShowBoss("BOSS • POSTURE BROKEN", new Color(1f, 0.78f, 0.3f));
                    break;
            }
        }

        private void ShowBossAttack(AttackDefinition attack)
        {
            _shownBossAttack = attack;
            ShowBoss(
                $"BOSS • {attack.DisplayName.ToUpperInvariant()} [{MLBossBrain.ClassifyArchetype(attack)}]",
                attack.Unblockable ? new Color(1f, 0.22f, 0.18f) : new Color(1f, 0.58f, 0.3f));
        }

        private void Show(string content, Color colour)
        {
            if (_label != null) { _label.text = content; _label.color = colour; }
            _remaining = HoldSeconds;
            if (_group != null) _group.alpha = 1f;
        }

        private void ShowBoss(string content, Color colour)
        {
            if (_bossLabel != null) { _bossLabel.text = content; _bossLabel.color = colour; }
            _bossRemaining = BossHoldSeconds;
            if (_bossGroup != null) _bossGroup.alpha = 1f;
        }

        private static void Fade(CanvasGroup group, ref float remaining)
        {
            if (remaining <= 0f || group == null) return;
            remaining -= Time.unscaledDeltaTime;
            group.alpha = remaining > FadeSeconds ? 1f : Mathf.Clamp01(remaining / FadeSeconds);
        }

        /// <summary>Creates a second independently fading line without requiring old scenes to be rebuilt.</summary>
        private void BuildBossCallout()
        {
            if (_label == null) return;

            // The generated scene originally put the CanvasGroup on the shared root. Move fading to
            // the player label so the new boss line can remain visible independently.
            CanvasGroup rootGroup = _group;
            if (rootGroup != null) rootGroup.alpha = 1f;

            _group = _label.GetComponent<CanvasGroup>();
            if (_group == null) _group = _label.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;

            _bossLabel = Instantiate(_label, _label.transform.parent);
            _bossLabel.name = "BossLabel";
            _bossLabel.fontSize = 25;
            _bossLabel.text = string.Empty;
            _bossLabel.rectTransform.anchoredPosition = _label.rectTransform.anchoredPosition + new Vector2(0f, 52f);

            _bossGroup = _bossLabel.GetComponent<CanvasGroup>();
            if (_bossGroup == null) _bossGroup = _bossLabel.gameObject.AddComponent<CanvasGroup>();
            _bossGroup.blocksRaycasts = false;
        }
    }
}
