using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    ///
    /// Priors are loaded at startup from StreamingAssets/utility_agent_config.json
    /// (trained from 3,444 frames of real playtester telemetry).
    /// </summary>
    public sealed class MLBossBrain
    {
        private readonly Stopwatch _timer = new Stopwatch();

        // =====================================================================
        // CONFIG LOADER — Reads Python-trained priors from StreamingAssets JSON
        // =====================================================================

        /// <summary>
        /// Minimal JSON config structure. Unity has no built-in JSON deserializer for
        /// nested dicts, so we parse the keys we need manually via JsonUtility with
        /// a flat wrapper. We fall back to hardcoded defaults if the file is missing.
        /// </summary>
        [Serializable]
        private class BrainConfig
        {
            public float lambda_risk_penalty   = 1.25f;
            public float online_learning_rate  = 0.25f;
            public float softmax_temperature   = 3.5f;
            public float ranged_min_cooldown   = 5.0f;
            public float heavy_crush_min_cooldown = 3.5f;
            public float repetition_penalty_per_repeat = 16.0f;

            // Per-archetype arrays in archetype order: [Melee, Ranged, Gap, Heavy]
            public float[] hit_prior_values    = { 0.2342f, 0.5199f, 0.1058f, 0.4218f };
            public float[] parry_prior_values  = { 0.2601f, 0.0159f, 0.0511f, 0.1034f };
            public float[] base_damage_values  = { 25f,     18f,     15f,     45f      };
            public float[] stamina_cost_values = { 10f,     8f,      12f,     25f      };
            public float[] posture_pen_values  = { 40f,     0f,      20f,     15f      };
        }

        private static BrainConfig LoadConfig()
        {
            string configPath = Path.Combine(Application.streamingAssetsPath, "utility_agent_config.json");
            BrainConfig cfg = new BrainConfig(); // Defaults (safe fallback)

            try
            {
                if (File.Exists(configPath))
                {
                    string raw = File.ReadAllText(configPath);

                    // Parse scalar floats via simple string search (no extra libs needed)
                    cfg.lambda_risk_penalty            = ParseJsonFloat(raw, "lambda_risk_penalty",            cfg.lambda_risk_penalty);
                    cfg.online_learning_rate           = ParseJsonFloat(raw, "online_learning_rate",           cfg.online_learning_rate);
                    cfg.softmax_temperature            = ParseJsonFloat(raw, "softmax_temperature",            cfg.softmax_temperature);
                    cfg.ranged_min_cooldown            = ParseJsonFloat(raw, "ranged_min_cooldown",            cfg.ranged_min_cooldown);
                    cfg.heavy_crush_min_cooldown       = ParseJsonFloat(raw, "heavy_crush_min_cooldown",       cfg.heavy_crush_min_cooldown);
                    cfg.repetition_penalty_per_repeat  = ParseJsonFloat(raw, "repetition_penalty_per_repeat",  cfg.repetition_penalty_per_repeat);

                    // Parse per-archetype prior dicts — order: Melee, Ranged, Gap, Heavy
                    string[] archetypes = { "MeleeStrike", "RangedThreat", "GapCloser", "HeavyCrush" };

                    for (int i = 0; i < archetypes.Length; i++)
                    {
                        cfg.hit_prior_values[i]    = ParseJsonDictFloat(raw, "hit_priors",    archetypes[i], cfg.hit_prior_values[i]);
                        cfg.parry_prior_values[i]  = ParseJsonDictFloat(raw, "parry_priors",  archetypes[i], cfg.parry_prior_values[i]);
                        cfg.base_damage_values[i]  = ParseJsonDictFloat(raw, "base_damages",  archetypes[i], cfg.base_damage_values[i]);
                        cfg.stamina_cost_values[i] = ParseJsonDictFloat(raw, "stamina_costs", archetypes[i], cfg.stamina_cost_values[i]);
                        cfg.posture_pen_values[i]  = ParseJsonDictFloat(raw, "posture_penalties", archetypes[i], cfg.posture_pen_values[i]);
                    }

                    UnityEngine.Debug.Log("<color=#00FF88>[ML BRAIN CONFIG]</color> Loaded Python-trained priors from StreamingAssets/utility_agent_config.json — " +
                                          $"Hit priors: Melee={cfg.hit_prior_values[0]:P0}, Ranged={cfg.hit_prior_values[1]:P0}, Gap={cfg.hit_prior_values[2]:P0}, Heavy={cfg.hit_prior_values[3]:P0}");
                }
                else
                {
                    UnityEngine.Debug.LogWarning("[ML BRAIN CONFIG] utility_agent_config.json not found in StreamingAssets — using hardcoded defaults.");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[ML BRAIN CONFIG] Failed to parse config: {ex.Message} — using defaults.");
            }

            return cfg;
        }

        /// Parses a top-level float from raw JSON text, e.g. "lambda_risk_penalty": 1.25
        private static float ParseJsonFloat(string json, string key, float fallback)
        {
            string search = $"\"{key}\"";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return fallback;

            int colon = json.IndexOf(':', idx + search.Length);
            if (colon < 0) return fallback;

            int start = colon + 1;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t' || json[start] == '\r' || json[start] == '\n'))
                start++;

            int end = start;
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\r' && json[end] != '\n')
                end++;

            string token = json.Substring(start, end - start).Trim();
            return float.TryParse(token, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float val) ? val : fallback;
        }

        /// Parses a value from a named nested object: "dictKey": { "fieldKey": value }
        private static float ParseJsonDictFloat(string json, string dictKey, string fieldKey, float fallback)
        {
            string dictSearch = $"\"{dictKey}\"";
            int dictIdx = json.IndexOf(dictSearch, StringComparison.Ordinal);
            if (dictIdx < 0) return fallback;

            int braceOpen = json.IndexOf('{', dictIdx);
            int braceClose = json.IndexOf('}', braceOpen);
            if (braceOpen < 0 || braceClose < 0) return fallback;

            string subJson = json.Substring(braceOpen, braceClose - braceOpen + 1);
            return ParseJsonFloat(subJson, fieldKey, fallback);
        }

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
        public bool ConfigLoadedFromFile { get; private set; }

        // Live action utility cache for UI inspection
        public readonly Dictionary<ActionArchetype, float> CurrentUtilities = new Dictionary<ActionArchetype, float>
        {
            { ActionArchetype.MeleeStrike, 0f },
            { ActionArchetype.RangedThreat, 0f },
            { ActionArchetype.GapCloser, 0f },
            { ActionArchetype.HeavyCrush, 0f }
        };

        // Bayesian Belief Vectors — Loaded from Python-trained priors in utility_agent_config.json
        public readonly Dictionary<ActionArchetype, float> CurrentParryBeliefs;
        public readonly Dictionary<ActionArchetype, float> CurrentHitBeliefs;

        // ---------------------------------------------------------------------
        // COMBAT REWARD & UTILITY PARAMETERS (Loaded from Python Track B)
        // ---------------------------------------------------------------------
        private readonly float _riskAversionWeight;  // Lambda penalizing parry vulnerability
        private readonly float _onlineLearningRate;  // Alpha for online exponential belief updates
        private readonly float _softmaxTemperature;
        private readonly float _rangedMinCooldown;
        private readonly float _heavyCrushMinCooldown;
        private readonly float _repetitionPenaltyPerRepeat;

        private readonly float[] _baseDamage    = new float[4];
        private readonly float[] _posturePenalty = new float[4];
        private readonly float[] _staminaCost    = new float[4];

        // ---------------------------------------------------------------------
        // CONSTRUCTOR — Loads Python-trained config from StreamingAssets
        // ---------------------------------------------------------------------
        public MLBossBrain()
        {
            BrainConfig cfg = LoadConfig();
            ConfigLoadedFromFile = File.Exists(
                Path.Combine(Application.streamingAssetsPath, "utility_agent_config.json"));

            // Load scalar hyper-parameters
            _riskAversionWeight         = cfg.lambda_risk_penalty;
            _onlineLearningRate         = cfg.online_learning_rate;
            _softmaxTemperature         = cfg.softmax_temperature;
            _rangedMinCooldown          = cfg.ranged_min_cooldown;
            _heavyCrushMinCooldown      = cfg.heavy_crush_min_cooldown;
            _repetitionPenaltyPerRepeat = cfg.repetition_penalty_per_repeat;

            // Load per-archetype arrays
            for (int i = 0; i < 4; i++)
            {
                _baseDamage[i]     = cfg.base_damage_values[i];
                _posturePenalty[i] = cfg.posture_pen_values[i];
                _staminaCost[i]    = cfg.stamina_cost_values[i];
            }

            // Load Python-trained belief priors
            CurrentHitBeliefs = new Dictionary<ActionArchetype, float>
            {
                { ActionArchetype.MeleeStrike,  cfg.hit_prior_values[0] },
                { ActionArchetype.RangedThreat, cfg.hit_prior_values[1] },
                { ActionArchetype.GapCloser,    cfg.hit_prior_values[2] },
                { ActionArchetype.HeavyCrush,   cfg.hit_prior_values[3] }
            };

            CurrentParryBeliefs = new Dictionary<ActionArchetype, float>
            {
                { ActionArchetype.MeleeStrike,  cfg.parry_prior_values[0] },
                { ActionArchetype.RangedThreat, cfg.parry_prior_values[1] },
                { ActionArchetype.GapCloser,    cfg.parry_prior_values[2] },
                { ActionArchetype.HeavyCrush,   cfg.parry_prior_values[3] }
            };
        }

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
        private float _lastParryRollActionStartedAt = float.NegativeInfinity;

        // Anti-Spam, Fatigue & Pacing Limits (Prevents degenerate projectile spamming)
        private ActionArchetype _lastExecutedArchetype = ActionArchetype.MeleeStrike;
        private int _consecutiveArchetypeCount = 0;
        private readonly float[] _lastArchetypeTime = { -999f, -999f, -999f, -999f };

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
            List<AttackDefinition> candidates = new List<AttackDefinition>();
            List<ActionArchetype> archetypes = new List<ActionArchetype>();
            List<float> utilities = new List<float>();
            List<string> explanations = new List<string>();

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
                float immediateUtility = (qHit * _baseDamage[k]) - (_riskAversionWeight * pParry * _posturePenalty[k]) - _staminaCost[k];

                // 5. 2-Step Lookahead Combo Planning (A* Search heuristic)
                float lookaheadBonus = 0f;
                if (archetype == ActionArchetype.GapCloser && distance > 3.5f)
                {
                    float predictedDist = Mathf.Max(1.5f, distance - 3.5f);
                    float predictedFollowupUtility = (CurrentHitBeliefs[ActionArchetype.HeavyCrush] * _baseDamage[(int)ActionArchetype.HeavyCrush]) - _staminaCost[(int)ActionArchetype.HeavyCrush];
                    lookaheadBonus = 0.85f * Mathf.Max(0f, predictedFollowupUtility * 0.35f);
                }

                // 6. Anti-Spam Cadence Penalties & Closing Aggression
                float timeSinceLast = currentTime - _lastArchetypeTime[k];
                float cooldownPenalty = 0f;
                if (archetype == ActionArchetype.RangedThreat && timeSinceLast < _rangedMinCooldown)
                {
                    // Strict cooldown on projectile waves — loaded from config
                    cooldownPenalty = (_rangedMinCooldown - timeSinceLast) * 12.0f + 30.0f;
                }
                else if (archetype == ActionArchetype.HeavyCrush && timeSinceLast < _heavyCrushMinCooldown)
                {
                    cooldownPenalty = (_heavyCrushMinCooldown - timeSinceLast) * 8.0f + 15.0f;
                }

                // Consecutive repetition penalty (Law of Diminishing Marginal Utility)
                float repeatPenalty = 0f;
                if (archetype == _lastExecutedArchetype)
                {
                    repeatPenalty = _repetitionPenaltyPerRepeat * _consecutiveArchetypeCount;
                }

                // Active Closing Drive: When far away, boss aggressively dashes forward with sword rather than camping
                float engageBonus = (distance > 3.8f && archetype == ActionArchetype.GapCloser) ? 14.0f : 0f;

                float totalUtility = immediateUtility + lookaheadBonus + engageBonus - cooldownPenalty - repeatPenalty;
                CurrentUtilities[archetype] = totalUtility;

                candidates.Add(candidate);
                archetypes.Add(archetype);
                utilities.Add(totalUtility);
                explanations.Add($"Utility={totalUtility:F1} (Q_Hit={qHit:P0}, P_Parry={pParry:P0}, Dist={distance:F1}m, Repeat={repeatPenalty:F0}, Cd={cooldownPenalty:F0})");
            }

            // Boltzmann / Softmax Exploration over candidate utilities to ensure variety and eliminate spam
            AttackDefinition bestAttack;
            ActionArchetype bestArchetype;
            float bestUtility;
            string bestExplanation;

            if (candidates.Count > 0)
            {
                float maxU = float.MinValue;
                for (int i = 0; i < utilities.Count; i++)
                {
                    if (utilities[i] > maxU) maxU = utilities[i];
                }

                float sumExp = 0f;
                float[] expScores = new float[utilities.Count];
                for (int i = 0; i < utilities.Count; i++)
                {
                    expScores[i] = Mathf.Exp((utilities[i] - maxU) / _softmaxTemperature);
                    sumExp += expScores[i];
                }

                float roll = (context.Random != null ? context.Random.NextFloat01() : UnityEngine.Random.value) * sumExp;
                float accum = 0f;
                int selectedIndex = 0;
                for (int i = 0; i < expScores.Length; i++)
                {
                    accum += expScores[i];
                    if (roll <= accum)
                    {
                        selectedIndex = i;
                        break;
                    }
                }

                bestAttack = candidates[selectedIndex];
                bestArchetype = archetypes[selectedIndex];
                bestUtility = utilities[selectedIndex];
                bestExplanation = explanations[selectedIndex];
            }
            else
            {
                bestAttack = catalogue[0];
                bestArchetype = ClassifyArchetype(bestAttack);
                bestUtility = 0f;
                bestExplanation = "Fallback (Catalogue Default)";
            }

            // Update repetition and cadence trackers
            if (bestArchetype == _lastExecutedArchetype)
            {
                _consecutiveArchetypeCount++;
            }
            else
            {
                _consecutiveArchetypeCount = 1;
                _lastExecutedArchetype = bestArchetype;
            }
            _lastArchetypeTime[(int)bestArchetype] = currentTime;

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
            if (!obs.IsValid)
            {
                return false;
            }

            bool heavy = obs.ActionState == ObservableActionState.HeavyAttacking;
            bool repeatedLight = obs.ActionState == ObservableActionState.LightAttacking &&
                                 LivePlayerAttackRate >= 0.6f;
            if (!heavy && !repeatedLight) return false;

            float distance = context.DistanceToPlayer;
            if (distance > 2.8f)
            {
                return false;
            }

            // One decision per visible swing. Rolling every frame would turn even a modest
            // probability into a near-certain parry during a long wind-up.
            if (Mathf.Abs(obs.ActionStartedAt - _lastParryRollActionStartedAt) < 0.02f)
                return false;
            _lastParryRollActionStartedAt = obs.ActionStartedAt;

            float adaptiveParryChance = heavy
                ? Mathf.Clamp(0.20f + (LivePlayerAttackRate * 0.10f), 0.20f, 0.60f)
                : Mathf.Clamp(0.10f + (LivePlayerAttackRate * 0.05f), 0.10f, 0.35f);
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
            CurrentParryBeliefs[archetype] = Mathf.Clamp01(oldParry * (1f - _onlineLearningRate) + (_onlineLearningRate * 1.0f));

            float oldHit = CurrentHitBeliefs[archetype];
            CurrentHitBeliefs[archetype] = Mathf.Clamp01(oldHit * (1f - _onlineLearningRate) + (_onlineLearningRate * 0.0f));

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
                else if (combatEvent.Kind == CombatEventKind.DodgePerformed)
                {
                    // A perfect dodge is the successful outcome of the dodge already recorded at
                    // dash start. Counting it again falsely doubles the player's dodge cadence.
                    _playerDodgeTimestamps.Enqueue(combatTime);
                }
                else if (combatEvent.Kind == CombatEventKind.Deflected)
                {
                    // A parry strike deliberately emits Deflected for existing effects plus
                    // PlayerParried for its richer semantics. It is still one defensive success.
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
                        CurrentHitBeliefs[archetype] = Mathf.Clamp01(CurrentHitBeliefs[archetype] * (1f - _onlineLearningRate) + (_onlineLearningRate * 1.0f));
                        CurrentParryBeliefs[archetype] = Mathf.Clamp01(CurrentParryBeliefs[archetype] * (1f - _onlineLearningRate) + (_onlineLearningRate * 0.0f));
                        TotalOnlineAdaptationUpdates++;
                    }
                }
                else if (combatEvent.Kind == CombatEventKind.AttackWhiffed)
                {
                    if (_lastExecutedAttack != null)
                    {
                        ActionArchetype archetype = ClassifyArchetype(_lastExecutedAttack);
                        // Attack whiffed: reduce hit belief slightly
                        CurrentHitBeliefs[archetype] = Mathf.Clamp01(CurrentHitBeliefs[archetype] * (1f - (_onlineLearningRate * 0.5f)));
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
                    // Only for long range (>4.2m). Heavily penalized in melee/mid range so boss does NOT projectile-camp
                    if (distance >= 4.2f) return 1.0f;
                    if (distance >= 3.2f) return 0.30f;
                    return 0.10f; // Strongly discouraged at point-blank (<3.2m)

                case ActionArchetype.GapCloser:
                    // Strongly favored at long range (>3.5m) to aggressively close distance!
                    if (distance > 3.5f) return 1.35f;
                    if (distance <= 2.0f) return 0.15f;
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
