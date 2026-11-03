using System.Diagnostics;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Core.Perception;
using UnityEngine;

namespace AdaptiveBossArena.AI
{
    /// <summary>
    /// Deterministic Rule-Based Expert System for Boss Decision Making.
    /// Operates purely on instantaneous physical state (distance, player state, vitals)
    /// without learning, memory, or adaptation to player habits.
    /// Forms the Academic Baseline for CSE3705 Comparative Evaluation.
    /// </summary>
    public sealed class RuleBasedBossBrain
    {
        private readonly Stopwatch _timer = new Stopwatch();

        /// <summary>Last rule evaluation execution time in microseconds (µs).</summary>
        public double LastExecutionTimeMicroseconds { get; private set; }

        /// <summary>Human-readable name of the last rule triggered in the priority hierarchy.</summary>
        public string LastTriggeredRule { get; private set; } = "None";

        /// <summary>Display name of the last chosen attack.</summary>
        public string LastChosenAttackName { get; private set; } = "None";

        /// <summary>
        /// Evaluates the priority rule chain to select the next combat action.
        /// </summary>
        /// <param name="context">Instantaneous sensory and physical context of the boss.</param>
        /// <returns>The deterministic attack chosen by the rule hierarchy.</returns>
        public AttackDefinition SelectAttack(BossContext context)
        {
            _timer.Restart();

            AttackDefinition[] catalogue = context.CurrentPhase.Attacks;
            if (catalogue == null || catalogue.Length == 0)
            {
                _timer.Stop();
                LastExecutionTimeMicroseconds = _timer.Elapsed.TotalMilliseconds * 1000.0;
                LastTriggeredRule = "Empty Catalogue";
                return null;
            }

            float distance = context.DistanceToPlayer;
            PlayerObservation observation = context.Perceived;
            ObservableActionState playerAction = observation.IsValid ? observation.ActionState : ObservableActionState.Idle;
            float playerHpNorm = observation.IsValid ? observation.NormalizedHealth : 1f;
            float bossHpNorm = context.Health != null ? context.Health.Normalized : 1f;

            // Attack resolution from phase catalogue
            AttackDefinition charge = FindAttack(catalogue, "Charge");
            AttackDefinition jab = FindAttack(catalogue, "Jab");
            AttackDefinition sweep = FindAttack(catalogue, "Wide Sweep", "Sweep");
            AttackDefinition slam = FindAttack(catalogue, "Ground Slam", "Slam");
            AttackDefinition perilous = FindAttack(catalogue, "Perilous Overhead", "Overhead");
            AttackDefinition shockwave = FindAttack(catalogue, "Shockwave", "Cataclysmic");

            AttackDefinition chosen = null;
            string ruleTag = "None";

            // =========================================================
            // RULE-BASED PRIORITY CHAIN (EXPERT SYSTEM)
            // =========================================================

            // [Rule 1] Long Range Engagement (Distance > 5.0m)
            // Motivation: Close the distance immediately using lunging attack
            if (distance > 5.0f)
            {
                ruleTag = "Rule 1 (Long Range: Dist > 5.0m)";
                chosen = charge ?? shockwave ?? catalogue[0];
            }
            // [Rule 2] Exploit Vulnerability / Guard (Distance <= 3.4m)
            // Motivation: Punish guarding turtles, knocked down players, or critical health
            else if (distance <= 3.4f && (playerAction == ObservableActionState.Guarding ||
                                          playerAction == ObservableActionState.KnockedDown ||
                                          playerAction == ObservableActionState.Healing ||
                                          playerHpNorm < 0.25f))
            {
                ruleTag = $"Rule 2 (Punish Vulnerable/Guarding: {playerAction})";
                chosen = perilous ?? slam ?? jab ?? catalogue[0];
            }
            // [Rule 3] Critical Health Emergency Area Denial (Distance <= 3.0m & Boss HP < 40%)
            // Motivation: Create breathing room when endangered
            else if (distance <= 3.0f && bossHpNorm < 0.40f)
            {
                ruleTag = "Rule 3 (Low HP Defense: Boss HP < 40%)";
                chosen = shockwave ?? slam ?? jab ?? catalogue[0];
            }
            // [Rule 4] Close Range Melee Pressure (Distance <= 2.8m)
            // Motivation: Fast strike to disrupt player wind-up
            else if (distance <= 2.8f)
            {
                ruleTag = "Rule 4 (Close Range Melee: Dist <= 2.8m)";
                // Alternate between fast jab and ground slam deterministically
                chosen = (Time.frameCount % 2 == 0 ? jab : slam) ?? catalogue[0];
            }
            // [Rule 5] Mid Range Spatial Control (2.8m < Distance <= 5.0m)
            // Motivation: Cover wide arc to catch sidestepping players
            else
            {
                ruleTag = "Rule 5 (Mid Range Sweep: 2.8m < Dist <= 5.0m)";
                chosen = sweep ?? slam ?? catalogue[0];
            }

            // Fallback safety to guarantee non-null from catalogue
            if (chosen == null)
            {
                ruleTag = "Fallback (Catalogue First)";
                chosen = catalogue[0];
            }

            _timer.Stop();
            LastExecutionTimeMicroseconds = _timer.Elapsed.TotalMilliseconds * 1000.0;
            LastTriggeredRule = ruleTag;
            LastChosenAttackName = chosen.DisplayName;

            UnityEngine.Debug.Log($"<color=#00FFAA>[RULE-BASED AI]</color> <b>{ruleTag}</b> -> Executed: <b>{chosen.DisplayName}</b> (Dist: {distance:F1}m, PlayerState: {playerAction}, Latency: {LastExecutionTimeMicroseconds:F2} µs)");

            return chosen;
        }

        /// <summary>
        /// Evaluates whether the boss attempts a defensive parry using a deterministic rule.
        /// Fixed probability without adaptive reinforcement.
        /// </summary>
        public bool DecideParry(BossContext context)
        {
            if (context.AttackCooldownRemaining > 0f || context.ParryCooldownRemaining > 0f)
            {
                return false;
            }

            PlayerObservation observation = context.Perceived;
            if (!observation.IsValid)
            {
                return false;
            }

            // Fixed rule: Only attempt parry if player is in a committed heavy attack within 2.8m
            bool playerHeavyAttacking = observation.ActionState == ObservableActionState.HeavyAttacking;
            float distance = context.DistanceToPlayer;

            if (playerHeavyAttacking && distance <= 2.8f)
            {
                // Fixed 30% baseline chance, independent of player history
                bool rolled = context.Random.NextFloat(0f, 1f) < 0.30f;
                if (rolled)
                {
                    UnityEngine.Debug.Log("<color=#FFAA00>[RULE-BASED AI]</color> Parry Rule Fired: Attempting deflect against incoming player heavy attack!");
                }
                return rolled;
            }

            return false;
        }

        private static AttackDefinition FindAttack(AttackDefinition[] catalogue, params string[] nameTokens)
        {
            for (int i = 0; i < catalogue.Length; i++)
            {
                AttackDefinition att = catalogue[i];
                if (att == null) continue;
                for (int t = 0; t < nameTokens.Length; t++)
                {
                    if (att.DisplayName.IndexOf(nameTokens[t], System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        att.name.IndexOf(nameTokens[t], System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return att;
                    }
                }
            }
            return null;
        }
    }
}
