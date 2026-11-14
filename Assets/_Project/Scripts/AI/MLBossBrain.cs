using System;
using System.Collections.Generic;
using System.Diagnostics;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Perception;
using UnityEngine;

namespace AdaptiveBossArena.AI
{
    /// <summary>
    /// Tactical Archetypes for Boss Combat Actions.
    /// Maps to the academic formulation in CSE2032 ML and CSE3705 AI.
    /// </summary>
    public enum ActionArchetype
    {
        MeleeStrike = 0,   // Wide Sweep, Jab, Basic Combo
        RangedThreat = 1,  // Cataclysmic Wave, Shockwave (Unparryable Projectile)
        GapCloser = 2,     // Charge, Dashing Thrust (Lunge)
        HeavyCrush = 3     // Ground Slam, Perilous Overhead (Guard Crusher)
    }

    /// <summary>
    /// Real-Time Utility-Based Machine Learning Boss Brain with Live Online Adaptation.
    /// Implements:
    /// 1. Outcome-Conditioned Value Estimation: Q_hat(s, a) = P(Hit | s, a)
    /// 2. Parry Risk Estimation: P_parry(s, a)
    /// 3. Maximum Expected Utility (MEU) Decision Making:
    ///    U(s, a) = Q_hat(s, a) * Damage(a) - lambda * P_parry(s, a) * PostureLoss(a) - StaminaCost(a)
    /// 4. Online Habit Adaptation & Bayesian Belief Updating (Reinforcement loop from live outcomes)
    /// 5. 2-Step Combo Lookahead (A* Heuristic Planning)
    /// </summary>
    public sealed class MLBossBrain
    {
        private readonly Stopwatch _timer = new Stopwatch();

        // ---------------------------------------------------------------------
        // LIVE DIAGNOSTICS & TELEMETRY
        // ---------------------------------------------------------------------
        public double LastExecutionTimeMicroseconds { get; private set; }
        public string LastChosenAttackName { get; private set; } = "None";
        public ActionArchetype LastChosenArchetype { get; private set; } = ActionArchetype.MeleeStrike;
        public float LastChosenUtility { get; private set; }
        public string LastDecisionExplanation { get; private set; } = "Initializing...";
        public string InferredPlayerArchetype { get; private set; } = "Calibrating (Ayush Baseline)";
        public int TotalOnlineAdaptationUpdates { get; private set; }

        // Live action utility cache for UI inspection
        public readonly Dictionary<ActionArchetype, float> CurrentUtilities = new Dictionary<ActionArchetype, float>
        {
            { ActionArchetype.MeleeStrike, 0f },
            { ActionArchetype.RangedThreat, 0f },
            { ActionArchetype.GapCloser, 0f },
            { ActionArchetype.HeavyCrush, 0f }
        };

        public readonly Dictionary<ActionArchetype, float> CurrentParryBeliefs = new Dictionary<ActionArchetype, float>
        {
            { ActionArchetype.MeleeStrike, 0.25f },
            { ActionArchetype.RangedThreat, 0.00f },
            { ActionArchetype.GapCloser, 0.15f },
            { ActionArchetype.HeavyCrush, 0.05f }
        };

        public readonly Dictionary<ActionArchetype, float> CurrentHitBeliefs = new Dictionary<ActionArchetype, float>
        {
            { ActionArchetype.MeleeStrike, 0.45f },
            { ActionArchetype.RangedThreat, 0.55f },
            { ActionArchetype.GapCloser, 0.40f },
            { ActionArchetype.HeavyCrush, 0.50f }
        };

        // ---------------------------------------------------------------------
        // COMBAT REWARD & UTILITY PARAMETERS (Trained from Python Track B)
        // ---------------------------------------------------------------------
        private const float RiskAversionWeight = 1.25f; // Lambda penalizing parry vulnerability
        private const float OnlineLearningRate = 0.25f;  // Alpha for online exponential belief updates

        private static readonly float[] BaseDamage = { 25f, 18f, 15f, 45f };
        private static readonly float[] PosturePenalty = { 40f, 0f, 20f, 15f }; // Ranged shockwave is 0 (unparryable)
        private static readonly float[] StaminaCost = { 10f, 8f, 12f, 25f };

        // ---------------------------------------------------------------------
        // REAL-TIME ROLLING PLAYER HABIT TRACKER (5.0s window)
        // ---------------------------------------------------------------------
        private readonly Queue<float> _playerAttackTimestamps = new Queue<float>();
        private readonly Queue<float> _playerDodgeTimestamps = new Queue<float>();
        private readonly Queue<float> _playerDeflectTimestamps = new Queue<float>();
        private const float RollingWindowSeconds = 5.0f;

        public float LivePlayerAttackRate { get; private set; }
        public float LivePlayerDodgeRate { get; private set; }
        public float LivePlayerDeflectRate { get; private set; }

        private AttackDefinition _lastExecutedAttack;

        /// <summary>
        /// Selects the optimal attack maximizing Expected Utility U(s, a) against current player habits.
        /// </summary>
        public AttackDefinition SelectAttack(BossContext context)
        {
            _timer.Restart();

            AttackDefinition[] catalogue = context.CurrentPhase.Attacks;
            if (catalogue == null || catalogue.Length == 0)
            {
                _timer.Stop();
                LastExecutionTimeMicroseconds = _timer.Elapsed.TotalMilliseconds * 1000.0;
                LastDecisionExplanation = "Empty Catalogue";
                return null;
            }

            float currentTime = context.Time != null ? context.Time.CombatTime : Time.time;
            UpdatePlayerCadenceRates(currentTime);

            float distance = context.DistanceToPlayer;
            PlayerObservation obs = context.Perceived;
            ObservableActionState playerState = obs.IsValid ? obs.ActionState : ObservableActionState.Idle;
            float playerHpNorm = obs.IsValid ? obs.NormalizedHealth : 1f;
            float bossHpNorm = context.Health != null ? context.Health.Normalized : 1f;

            // Score each candidate attack using the learned Value and Utility Function
            AttackDefinition bestAttack = null;
            float bestUtility = -float.MaxValue;
            ActionArchetype bestArchetype = ActionArchetype.MeleeStrike;
            string bestExplanation = "";

            for (int i = 0; i < catalogue.Length; i++)
            {
                AttackDefinition candidate = catalogue[i];
                if (candidate == null) continue;

                ActionArchetype archetype = ClassifyArchetype(candidate);
                int k = (int)archetype;

                // 1. Kinematic Spatial Compatibility Factor
                float spatialFactor = ComputeSpatialCompatibility(archetype, distance);

                // 2. State-Action Value (Hit Probability) Q_hat(s, a)
                float qHit = CurrentHitBeliefs[archetype] * spatialFactor;

                // If player is vulnerable or healing, close-range attacks connect with higher certainty
                if (playerState == ObservableActionState.Guarding ||
                    playerState == ObservableActionState.KnockedDown ||
                    playerState == ObservableActionState.Healing ||
                    playerHpNorm < 0.25f)
                {
                    if (archetype == ActionArchetype.HeavyCrush)
                    {
                        qHit = Mathf.Min(0.95f, qHit + 0.35f); // Punish turtles with guard crush
                    }
                    else if (archetype == ActionArchetype.MeleeStrike)
                    {
                        qHit = Mathf.Min(0.90f, qHit + 0.20f);
                    }
                }

                // If player is actively dodging frequently, discount direct melee
                if (LivePlayerDodgeRate > 0.20f && archetype == ActionArchetype.MeleeStrike)
                {
                    qHit *= Mathf.Max(0.40f, 1f - LivePlayerDodgeRate);
                }

                // 3. Dynamic Parry Risk P_parry(s, a)
                float pParry;
                if (archetype == ActionArchetype.RangedThreat)
                {
                    pParry = 0.0f; // Shockwave projectile cannot be parried!
                }
                else
                {
                    // Scale parry risk directly with player's real-time deflect cadence
                    float parryMultiplier = 1.0f + (LivePlayerDeflectRate * 3.0f);
                    pParry = Mathf.Clamp01(CurrentParryBeliefs[archetype] * parryMultiplier);

                    // If player is a known parry specialist (Swam), heavily penalize melee strike
                    if (LivePlayerDeflectRate > 0.20f && archetype == ActionArchetype.MeleeStrike)
                    {
                        pParry = Mathf.Max(0.45f, pParry);
                    }
                }

                // 4. Expected Utility Formulation
                float immediateUtility = (qHit * BaseDamage[k]) - (RiskAversionWeight * pParry * PosturePenalty[k]) - StaminaCost[k];

                // 5. 2-Step Lookahead Combo Planning (A* Search heuristic)
                float lookaheadBonus = 0f;
                if (archetype == ActionArchetype.GapCloser && distance > 3.5f)
                {
                    // After closing distance, follow-up Ground Slam or Melee is enabled
                    float predictedDist = Mathf.Max(1.5f, distance - 3.5f);
                    float predictedFollowupUtility = (CurrentHitBeliefs[ActionArchetype.HeavyCrush] * BaseDamage[(int)ActionArchetype.HeavyCrush]) - StaminaCost[(int)ActionArchetype.HeavyCrush];
                    lookaheadBonus = 0.85f * Mathf.Max(0f, predictedFollowupUtility * 0.35f);
                }

                float totalUtility = immediateUtility + lookaheadBonus;
                CurrentUtilities[archetype] = totalUtility;

                if (totalUtility > bestUtility)
                {
                    bestUtility = totalUtility;
                    bestAttack = candidate;
                    bestArchetype = archetype;
                    bestExplanation = $"Utility={totalUtility:F1} (Q_Hit={qHit:P0}, P_Parry={pParry:P0}, Dist={distance:F1}m)";
                }
            }

            // Fallback safety
            if (bestAttack == null)
            {
                bestAttack = catalogue[0];
                bestArchetype = ClassifyArchetype(bestAttack);
                bestUtility = 0f;
                bestExplanation = "Fallback (Catalogue Default)";
            }

            _timer.Stop();
            LastExecutionTimeMicroseconds = _timer.Elapsed.TotalMilliseconds * 1000.0;
            LastChosenAttackName = bestAttack.DisplayName;
            LastChosenArchetype = bestArchetype;
            LastChosenUtility = bestUtility;
            LastDecisionExplanation = bestExplanation;
            _lastExecutedAttack = bestAttack;

            UnityEngine.Debug.Log($"<color=#FF00AA>[LIVE ML BRAIN]</color> Selected: <b>{bestAttack.DisplayName}</b> [{bestArchetype}] | {bestExplanation} | Latency: {LastExecutionTimeMicroseconds:F2} µs | Player: {InferredPlayerArchetype}");

            return bestAttack;
        }

        /// <summary>
        /// Adaptive parry decision based on player's observed attack aggression.
        /// </summary>
        public bool DecideParry(BossContext context)
        {
            if (context.AttackCooldownRemaining > 0f || context.ParryCooldownRemaining > 0f)
            {
                return false;
            }

            PlayerObservation obs = context.Perceived;
            if (!obs.IsValid || obs.ActionState != ObservableActionState.HeavyAttacking)
            {
                return false;
            }

            float distance = context.DistanceToPlayer;
            if (distance > 2.8f)
            {
                return false;
            }

            // Dynamic parry probability conditioned on player aggression rate
            // High aggression (Sivam brawler) -> boss increases parry rate to 60%
            // Low aggression (defensive turtler) -> boss parries selectively at 20%
            float adaptiveParryChance = Mathf.Clamp(0.20f + (LivePlayerAttackRate * 0.65f), 0.15f, 0.65f);
            bool rolled = context.Random.NextFloat(0f, 1f) < adaptiveParryChance;

            if (rolled)
            {
                UnityEngine.Debug.Log($"<color=#FF00AA>[LIVE ML DEFENSE]</color> Adaptive Parry Fired! (AggressionRate: {LivePlayerAttackRate:F2}/s, ParryChance: {adaptiveParryChance:P0})");
            }

            return rolled;
        }

        // =====================================================================
        // ONLINE LEARNING: REAL-TIME REINFORCEMENT & BELIEF UPDATING
        // =====================================================================

        /// <summary>
        /// Called when the boss's attack is parried/deflected by the player.
        /// Updates the parry belief vector in real-time, collapsing melee utility!
        /// </summary>
        public void OnAttackParried(AttackDefinition parriedAttack)
        {
            AttackDefinition att = parriedAttack ?? _lastExecutedAttack;
            if (att == null) return;

            ActionArchetype archetype = ClassifyArchetype(att);

            // Online Bayesian / EMA belief update
            float oldParry = CurrentParryBeliefs[archetype];
            CurrentParryBeliefs[archetype] = Mathf.Clamp01(oldParry * (1f - OnlineLearningRate) + (OnlineLearningRate * 1.0f));

            float oldHit = CurrentHitBeliefs[archetype];
            CurrentHitBeliefs[archetype] = Mathf.Clamp01(oldHit * (1f - OnlineLearningRate) + (OnlineLearningRate * 0.0f));

            TotalOnlineAdaptationUpdates++;

            UnityEngine.Debug.Log($"<color=#FF5555>[LIVE ML ADAPTATION]</color> <b>PARRY WITNESSED!</b> Player deflected {att.DisplayName} ({archetype}). " +
                                  $"Parry belief updated: {oldParry:P0} -> <b>{CurrentParryBeliefs[archetype]:P0}</b>. " +
                                  $"Hit belief reduced: {oldHit:P0} -> <b>{CurrentHitBeliefs[archetype]:P0}</b>. Total Updates: {TotalOnlineAdaptationUpdates}");
        }

        /// <summary>
        /// Processes witnessed combat events from the event bus to update rolling cadence and outcome beliefs.
        /// </summary>
        public void OnCombatEvent(in CombatEvent combatEvent, float combatTime)
        {
            // 1. Track player action cadence
            if (combatEvent.Actor == CombatantTeam.Player)
            {
                if (combatEvent.Kind == CombatEventKind.AttackStarted)
                {
                    _playerAttackTimestamps.Enqueue(combatTime);
                }
                else if (combatEvent.Kind == CombatEventKind.DodgePerformed || combatEvent.Kind == CombatEventKind.PerfectDodge)
                {
                    _playerDodgeTimestamps.Enqueue(combatTime);
                }
                else if (combatEvent.Kind == CombatEventKind.Deflected || combatEvent.Kind == CombatEventKind.Parried || combatEvent.Kind == CombatEventKind.PlayerParried)
                {
                    _playerDeflectTimestamps.Enqueue(combatTime);
                }
            }

            // 2. Track boss attack outcomes
            if (combatEvent.Actor == CombatantTeam.Boss)
            {
                if (combatEvent.Kind == CombatEventKind.AttackLanded)
                {
                    if (_lastExecutedAttack != null)
                    {
                        ActionArchetype archetype = ClassifyArchetype(_lastExecutedAttack);
                        // Attack successfully connected: reinforce hit belief and reward archetype
                        CurrentHitBeliefs[archetype] = Mathf.Clamp01(CurrentHitBeliefs[archetype] * (1f - OnlineLearningRate) + (OnlineLearningRate * 1.0f));
                        CurrentParryBeliefs[archetype] = Mathf.Clamp01(CurrentParryBeliefs[archetype] * (1f - OnlineLearningRate) + (OnlineLearningRate * 0.0f));
                        TotalOnlineAdaptationUpdates++;
                    }
                }
                else if (combatEvent.Kind == CombatEventKind.AttackWhiffed)
                {
                    if (_lastExecutedAttack != null)
                    {
                        ActionArchetype archetype = ClassifyArchetype(_lastExecutedAttack);
                        // Attack whiffed: reduce hit belief slightly
                        CurrentHitBeliefs[archetype] = Mathf.Clamp01(CurrentHitBeliefs[archetype] * (1f - (OnlineLearningRate * 0.5f)));
                        TotalOnlineAdaptationUpdates++;
                    }
                }
            }

            UpdatePlayerCadenceRates(combatTime);
        }

        // =====================================================================
        // HELPER FUNCTIONS & ARCHETYPE CLASSIFICATION
        // =====================================================================

        public static ActionArchetype ClassifyArchetype(AttackDefinition attack)
        {
            if (attack == null) return ActionArchetype.MeleeStrike;
            string name = (attack.DisplayName + " " + attack.name).ToLowerInvariant();

            if (name.Contains("shockwave") || name.Contains("wave") || name.Contains("cataclysmic") || name.Contains("ranged"))
                return ActionArchetype.RangedThreat;

            if (name.Contains("charge") || name.Contains("thrust") || name.Contains("lunge") || name.Contains("dash"))
                return ActionArchetype.GapCloser;

            if (name.Contains("slam") || name.Contains("ground") || name.Contains("overhead") || name.Contains("crush") || name.Contains("heavy"))
                return ActionArchetype.HeavyCrush;

            return ActionArchetype.MeleeStrike;
        }

        private static float ComputeSpatialCompatibility(ActionArchetype archetype, float distance)
        {
            switch (archetype)
            {
                case ActionArchetype.MeleeStrike:
                    // Effective within 2.8m, sharp exponential falloff beyond 3.0m
                    if (distance <= 2.8f) return 1.0f;
                    return Mathf.Exp(-0.75f * (distance - 2.8f));

                case ActionArchetype.RangedThreat:
                    // Highly effective across mid-to-long ranges (2.0m to 8.0m)
                    if (distance >= 2.0f && distance <= 7.5f) return 1.0f;
                    if (distance < 2.0f) return 0.70f;
                    return 0.60f;

                case ActionArchetype.GapCloser:
                    // Strongly favored at long range (>3.5m), penalized at point-blank range (<2.0m)
                    if (distance > 3.5f) return 1.15f;
                    if (distance <= 2.0f) return 0.25f;
                    return 0.65f;

                case ActionArchetype.HeavyCrush:
                    // Devastating close-to-mid range impact (1.0m to 3.5m)
                    if (distance <= 3.2f) return 1.0f;
                    if (distance <= 4.5f) return 0.50f;
                    return 0.20f;

                default:
                    return 1.0f;
            }
        }

        private void UpdatePlayerCadenceRates(float currentTime)
        {
            // Clean up sliding window queues older than 5.0 seconds
            PruneQueue(_playerAttackTimestamps, currentTime - RollingWindowSeconds);
            PruneQueue(_playerDodgeTimestamps, currentTime - RollingWindowSeconds);
            PruneQueue(_playerDeflectTimestamps, currentTime - RollingWindowSeconds);

            LivePlayerAttackRate = _playerAttackTimestamps.Count / RollingWindowSeconds;
            LivePlayerDodgeRate = _playerDodgeTimestamps.Count / RollingWindowSeconds;
            LivePlayerDeflectRate = _playerDeflectTimestamps.Count / RollingWindowSeconds;

            // Classify player habit in real time
            if (LivePlayerDeflectRate >= 0.20f)
            {
                InferredPlayerArchetype = $"Parry Specialist (Swam Cadence: {LivePlayerDeflectRate:F2} def/s)";
            }
            else if (LivePlayerDodgeRate >= 0.25f)
            {
                InferredPlayerArchetype = $"Evasive Dodger (Sidd Cadence: {LivePlayerDodgeRate:F2} dodge/s)";
            }
            else if (LivePlayerAttackRate >= 0.35f)
            {
                InferredPlayerArchetype = $"Aggressive Brawler (Sivam Cadence: {LivePlayerAttackRate:F2} atk/s)";
            }
            else
            {
                InferredPlayerArchetype = "Balanced Tactical (Ayush Baseline)";
            }
        }

        private static void PruneQueue(Queue<float> queue, float cutoffTime)
        {
            while (queue.Count > 0 && queue.Peek() < cutoffTime)
            {
                queue.Dequeue();
            }
        }
    }
}
