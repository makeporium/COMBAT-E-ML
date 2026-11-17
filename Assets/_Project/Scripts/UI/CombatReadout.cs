using AdaptiveBossArena.AI;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Services;
using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveBossArena.UI
{
    /// <summary>Shows the most recent visible choice made by each fighter and the ML boss's current read.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatReadout : MonoBehaviour
    {
        private const float RefreshSeconds = 0.15f;

        [SerializeField] private Text _playerAction;
        [SerializeField] private Text _bossAction;
        [SerializeField] private Text _mlStatus;
        private ICombatEventBus _events;
        private BossController _boss;
        private AttackDefinition _displayedBossAttack;
        private float _nextRefresh;

        public void Bind(Text playerAction, Text bossAction, Text mlStatus)
        {
            _playerAction = playerAction;
            _bossAction = bossAction;
            _mlStatus = mlStatus;
        }

        private void Start()
        {
            ResolveLabels();
            BindEvents();
            _boss = FindAnyObjectByType<BossController>();
            Set(_playerAction, "WAITING", new Color(0.72f, 0.78f, 0.86f));
            Set(_bossAction, "WAITING", new Color(0.92f, 0.63f, 0.42f));
        }

        private void OnDestroy()
        {
            if (_events != null) _events.EventRecorded -= OnCombatEvent;
        }

        private void Update()
        {
            BindEvents();
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            if (_boss == null) _boss = FindAnyObjectByType<BossController>();
            if (_boss == null) return;

            RefreshBossAttack();
            if (_mlStatus == null) return;

            if (_boss.AIMode != BossAIMode.MachineLearning)
            {
                _mlStatus.text = "RULE-BASED BASELINE\nNo live utility adaptation";
                return;
            }

            MLBossBrain brain = _boss.MLBrain;
            if (brain == null) return;
            _mlStatus.text = $"ML LIVE  •  {brain.InferredPlayerArchetype}\n" +
                             $"READ: D {brain.LivePlayerDodgeRate:F2}/s  |  F {brain.LivePlayerDeflectRate:F2}/s\n" +
                             $"CHOICE: {brain.LastChosenArchetype}  U {brain.LastChosenUtility:F1}";
        }

        private void ResolveLabels()
        {
            // Older generated scenes were saved before these references were serializable.
            foreach (Text label in GetComponentsInChildren<Text>(true))
            {
                if (label.name == "PlayerAction") _playerAction = label;
                else if (label.name == "BossAction") _bossAction = label;
                else if (label.name == "MlStatus") _mlStatus = label;
            }
        }

        private void BindEvents()
        {
            if (!ServiceRegistry.Current.TryGet<ICombatEventBus>(out var current) || current == _events) return;
            if (_events != null) _events.EventRecorded -= OnCombatEvent;
            _events = current;
            _events.EventRecorded += OnCombatEvent;
        }

        private void OnCombatEvent(CombatEvent combatEvent)
        {
            if (combatEvent.Actor == CombatantTeam.Player)
            {
                string action = PlayerAction(combatEvent.Kind, combatEvent.DamageType);
                if (action != null) Set(_playerAction, action, PlayerColour(combatEvent.Kind));
                return;
            }

            if (combatEvent.Actor == CombatantTeam.Boss)
            {
                string action = BossAction(combatEvent.Kind, combatEvent.DamageType);
                if (action != null) Set(_bossAction, action, BossColour(combatEvent.Kind));
            }
        }

        private string PlayerAction(CombatEventKind kind, DamageType type)
        {
            switch (kind)
            {
                case CombatEventKind.AttackStarted: return type == DamageType.Heavy ? "HEAVY ATTACK" : type == DamageType.Special ? "SPECIAL" : "LIGHT ATTACK";
                case CombatEventKind.DodgePerformed: return "DODGE / I-FRAMES";
                case CombatEventKind.PerfectDodge: return "PERFECT DODGE";
                case CombatEventKind.GuardRaised: return "GUARD / DEFLECT WINDOW";
                case CombatEventKind.PlayerParryStarted: return "PARRY STRIKE";
                case CombatEventKind.Deflected: return "CLEAN DEFLECT";
                case CombatEventKind.PlayerParried: return "PARRY STRIKE LANDED";
                case CombatEventKind.Blocked: return "LATE BLOCK";
                case CombatEventKind.Riposte: return "RIPOSTE";
                default: return null;
            }
        }

        private string BossAction(CombatEventKind kind, DamageType type)
        {
            switch (kind)
            {
                case CombatEventKind.AttackStarted:
                    AttackDefinition attack = _boss != null ? _boss.CurrentAttack : null;
                    _displayedBossAttack = attack;
                    return attack != null ? FormatBossAttack(attack) : "BOSS ATTACK";
                case CombatEventKind.AttackLanded: return "HIT CONFIRMED";
                case CombatEventKind.AttackEvaded: return "ATTACK EVADED";
                case CombatEventKind.AttackWhiffed: return "ATTACK WHIFFED";
                case CombatEventKind.Parried: return "PARRIED";
                default: return null;
            }
        }

        /// <summary>
        /// Mirrors the boss's active attack into the ledger even if the readout was enabled after
        /// the attack-start event. This also makes the UI resilient to scene initialisation order.
        /// </summary>
        private void RefreshBossAttack()
        {
            AttackDefinition activeAttack = _boss.CurrentAttack;

            if (activeAttack == null)
            {
                // Clear only the observation marker. The last outcome remains visible until the
                // next attack begins, while repeating the same move can still refresh the label.
                _displayedBossAttack = null;
                return;
            }

            if (activeAttack == _displayedBossAttack) return;

            _displayedBossAttack = activeAttack;
            Set(_bossAction, FormatBossAttack(activeAttack), BossColour(CombatEventKind.AttackStarted));
        }

        private static string FormatBossAttack(AttackDefinition attack) =>
            $"{attack.DisplayName}  •  {MLBossBrain.ClassifyArchetype(attack)}";

        private static void Set(Text target, string content, Color colour)
        {
            if (target == null) return;
            target.text = content;
            target.color = colour;
        }

        private static Color PlayerColour(CombatEventKind kind) =>
            kind == CombatEventKind.PerfectDodge || kind == CombatEventKind.Deflected || kind == CombatEventKind.PlayerParried
                ? new Color(0.48f, 0.88f, 1f) : new Color(0.82f, 0.84f, 0.9f);

        private static Color BossColour(CombatEventKind kind) =>
            kind == CombatEventKind.AttackLanded ? new Color(1f, 0.34f, 0.26f) : new Color(0.94f, 0.68f, 0.42f);
    }
}
