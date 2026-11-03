using System;
using System.Globalization;
using System.IO;
using System.Text;
using AdaptiveBossArena.AI;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Services;
using AdaptiveBossArena.Learning;
using AdaptiveBossArena.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// Records combat state, player vitals, boss adaptation features, tuning parameters,
    /// and event tallies into a persistent CSV dataset for machine learning.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generates and appends to <c>gameplay_telemetry.csv</c> in the project root directory.
    /// Each game/encounter receives an auto-incrementing <c>game_id</c>.
    /// Samples are captured periodically (every second), at every boss attack initiation,
    /// and on encounter conclusion.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TelemetryManager : MonoBehaviour
    {
        public static TelemetryManager Instance { get; private set; }

        private const string CsvFileName = "gameplay_telemetry.csv";

        [Header("Sampling Configuration")]
        [SerializeField]
        [Range(0.2f, 5f)]
        [Tooltip("Seconds between regular periodic telemetry snapshots.")]
        private float _sampleIntervalSeconds = 1.0f;

        [SerializeField]
        [Tooltip("When enabled, immediately logs a row whenever the boss initiates an attack.")]
        private bool _logOnBossAttack = true;

        [SerializeField]
        [Tooltip("Enables console status notifications when telemetry samples and games are recorded.")]
        private bool _verboseLogging = true;

        private PlayerController _player;
        private BossController _boss;
        private ITimeService _time;
        private ICombatEventBus _events;

        private string _csvPath;
        private StreamWriter _writer;
        private int _currentGameId = 1;
        private int _sampleCountThisGame = 0;
        private float _nextSampleTime = 0f;
        private float _matchStartTime = 0f;
        private float _lastBossHp = 650f;
        private float _lastPlayerHp = 100f;
        private string _lastPlayerState = "";
        private string _lastBossState = "";
        private AttackDefinition _lastObservedBossAttack;
        private bool _wasCombatActive = false;

        // Cumulative combat event counters for the current game
        private int _countPlayerAttacks;
        private int _countPlayerDodges;
        private int _countPlayerDeflects;
        private int _countPlayerBlocks;
        private int _countPlayerHitsTaken;
        private int _countBossAttacks;
        private int _countBossParries;
        private int _countBossHitsTaken;

        private const string CsvHeader =
            "game_id," +
            "timestamp," +
            "frame," +
            "encounter_status," +
            "distance_to_boss," +
            "relative_angle_deg," +
            "player_state," +
            "player_hp," +
            "player_hp_pct," +
            "player_stamina," +
            "player_stamina_pct," +
            "player_posture," +
            "player_posture_pct," +
            "player_is_guarding," +
            "player_is_invulnerable," +
            "boss_state," +
            "boss_action," +
            "boss_hp," +
            "boss_hp_pct," +
            "boss_poise," +
            "boss_poise_pct," +
            "boss_phase," +
            "feature_attack_frequency," +
            "feature_heavy_attack_ratio," +
            "feature_post_dodge_aggression," +
            "feature_dash_direction_bias," +
            "feature_preferred_distance," +
            "feature_aggression," +
            "feature_whiff_ratio," +
            "feature_heal_frequency," +
            "feature_dodge_timing_consistency," +
            "feature_evasion_success," +
            "feature_guard_reliance," +
            "feature_deflect_skill," +
            "tuning_attack_delay," +
            "tuning_preferred_range," +
            "tuning_parry_chance," +
            "tuning_gap_closer_weight," +
            "tuning_dodge_prediction_strength," +
            "tuning_aggression," +
            "tuning_combo_extension_chance," +
            "tuning_feint_chance," +
            "tuning_area_denial_weight," +
            "tuning_unblockable_weight," +
            "count_player_attacks," +
            "count_player_dodges," +
            "count_player_deflects," +
            "count_player_blocks," +
            "count_player_hits_taken," +
            "count_boss_attacks," +
            "count_boss_parries," +
            "count_boss_hits_taken";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoBootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var holder = new GameObject("--- Telemetry ---");
            DontDestroyOnLoad(holder);
            holder.AddComponent<TelemetryManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            InitializeFilePipeline();
        }

        private void Start()
        {
            FindReferences();
            if (_player != null && _boss != null)
            {
                StartNewGame();
            }
        }

        private void OnEnable()
        {
            EnsureWriter();
            SubscribeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
            CloseWriter();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            CloseWriter();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            CloseWriter();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _player = null;
            _boss = null;
            FindReferences();
            if (_player != null && _boss != null)
            {
                StartNewGame();
            }
        }

        private void EnsureWriter()
        {
            if (_writer == null)
            {
                InitializeFilePipeline();
            }
        }

        private void InitializeFilePipeline()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                _csvPath = Path.Combine(projectRoot, CsvFileName);

                bool fileExists = File.Exists(_csvPath);

                if (!fileExists)
                {
                    using (var initStream = new StreamWriter(_csvPath, false, Encoding.UTF8))
                    {
                        initStream.WriteLine(CsvHeader);
                    }
                    _currentGameId = 1;
                }
                else
                {
                    _currentGameId = DetermineNextGameId(_csvPath);
                }

                _writer = new StreamWriter(_csvPath, append: true, Encoding.UTF8)
                {
                    AutoFlush = true
                };

                Debug.Log($"[TelemetryManager] Initialized. Target CSV: '{_csvPath}'. Resuming at Game #{_currentGameId}.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TelemetryManager] Failed to initialize CSV file: {ex.Message}");
            }
        }

        private static int DetermineNextGameId(string path)
        {
            int maxId = 0;
            try
            {
                using (var reader = new StreamReader(path, Encoding.UTF8))
                {
                    string header = reader.ReadLine();
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        int comma = line.IndexOf(',');
                        if (comma > 0 && int.TryParse(line.Substring(0, comma), out int id))
                        {
                            if (id > maxId)
                            {
                                maxId = id;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TelemetryManager] Error reading existing game IDs: {ex.Message}");
            }

            return maxId + 1;
        }

        private void FindReferences()
        {
            if (_player == null)
            {
                _player = UnityEngine.Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
            }

            if (_boss == null)
            {
                _boss = UnityEngine.Object.FindAnyObjectByType<BossController>(FindObjectsInactive.Include);
            }

            if (_time == null && ServiceRegistry.Current.TryGet<ITimeService>(out var timeService))
            {
                _time = timeService;
            }

            if (_events == null && ServiceRegistry.Current.TryGet<ICombatEventBus>(out var eventBus))
            {
                _events = eventBus;
                _events.EventRecorded += OnCombatEvent;
            }

            if (_verboseLogging && _player != null && _boss != null)
            {
                Debug.Log($"[TelemetryManager] Bound references: Player='{_player.name}', Boss='{_boss.name}'. Ready for recording.");
            }
        }

        private void SubscribeEvents()
        {
            if (_events == null && ServiceRegistry.Current.TryGet<ICombatEventBus>(out var eventBus))
            {
                _events = eventBus;
                _events.EventRecorded += OnCombatEvent;
            }
        }

        private void UnsubscribeEvents()
        {
            if (_events != null)
            {
                _events.EventRecorded -= OnCombatEvent;
            }
        }

        private void OnCombatEvent(CombatEvent combatEvent)
        {
            if (combatEvent.Actor == CombatantTeam.Player)
            {
                switch (combatEvent.Kind)
                {
                    case CombatEventKind.AttackStarted:
                        _countPlayerAttacks++;
                        break;
                    case CombatEventKind.DodgePerformed:
                    case CombatEventKind.PerfectDodge:
                        _countPlayerDodges++;
                        break;
                    case CombatEventKind.Deflected:
                    case CombatEventKind.PlayerParried:
                        _countPlayerDeflects++;
                        break;
                    case CombatEventKind.Blocked:
                        _countPlayerBlocks++;
                        break;
                    case CombatEventKind.AttackLanded:
                        // Player attack landed on boss!
                        _countBossHitsTaken++;
                        break;
                }
            }
            else if (combatEvent.Actor == CombatantTeam.Boss)
            {
                switch (combatEvent.Kind)
                {
                    case CombatEventKind.AttackStarted:
                        _countBossAttacks++;
                        break;
                    case CombatEventKind.Parried:
                        _countBossParries++;
                        break;
                    case CombatEventKind.AttackLanded:
                        // Boss attack landed on player!
                        _countPlayerHitsTaken++;
                        break;
                }
            }
        }

        /// <summary>Starts a new match session, incrementing game_id and resetting counters.</summary>
        public void StartNewGame()
        {
            _countPlayerAttacks = 0;
            _countPlayerDodges = 0;
            _countPlayerDeflects = 0;
            _countPlayerBlocks = 0;
            _countPlayerHitsTaken = 0;
            _countBossAttacks = 0;
            _countBossParries = 0;
            _countBossHitsTaken = 0;
            _sampleCountThisGame = 0;
            _lastObservedBossAttack = null;
            _nextSampleTime = 0f;
            _wasCombatActive = true;
            _matchStartTime = Time.time;

            if (_boss != null && _boss.Health != null)
            {
                _lastBossHp = _boss.Health.Current;
            }
            if (_player != null && _player.Health != null)
            {
                _lastPlayerHp = _player.Health.Current;
            }
            _lastPlayerState = _player != null ? _player.CurrentStateName : "";
            _lastBossState = _boss != null ? _boss.CurrentStateName : "";

            if (_verboseLogging)
            {
                Debug.Log($"[TelemetryManager] >>> Started recording Game #{_currentGameId} <<<");
            }
        }

        private void Update()
        {
            if (_player == null || _boss == null)
            {
                FindReferences();
                if (_player == null || _boss == null)
                {
                    return;
                }
            }

            EnsureWriter();

            // Re-sync combat events if service registry refreshed
            if (ServiceRegistry.Current.TryGet<ICombatEventBus>(out var eventBus) && eventBus != _events)
            {
                if (_events != null) _events.EventRecorded -= OnCombatEvent;
                _events = eventBus;
                _events.EventRecorded += OnCombatEvent;
            }

            float currentTime = Mathf.Max(0f, Time.time - _matchStartTime);
            bool isCombatActive = _player.IsAlive && _boss.IsAlive;

            // Direct health delta detection to guarantee hits are never missed
            if (_boss.Health != null)
            {
                float curBossHp = _boss.Health.Current;
                if (curBossHp < _lastBossHp - 0.05f)
                {
                    _countBossHitsTaken++;
                    _lastBossHp = curBossHp;
                }
                else if (curBossHp > _lastBossHp)
                {
                    _lastBossHp = curBossHp;
                }
            }

            if (_player.Health != null)
            {
                float curPlayerHp = _player.Health.Current;
                if (curPlayerHp < _lastPlayerHp - 0.05f)
                {
                    _countPlayerHitsTaken++;
                    _lastPlayerHp = curPlayerHp;
                }
                else if (curPlayerHp > _lastPlayerHp)
                {
                    _lastPlayerHp = curPlayerHp;
                }
            }

            // Direct state machine tracking for attack inputs and dodges
            string playerState = _player.CurrentStateName;
            if (playerState == "PlayerAttackState" && _lastPlayerState != "PlayerAttackState")
            {
                _countPlayerAttacks++;
            }
            if (playerState == "PlayerDashState" && _lastPlayerState != "PlayerDashState")
            {
                _countPlayerDodges++;
            }
            _lastPlayerState = playerState;

            string bossState = _boss.CurrentStateName;
            if (bossState == "BossAttackState" && _lastBossState != "BossAttackState")
            {
                _countBossAttacks++;
            }
            if (bossState == "BossParryState" && _lastBossState != "BossParryState")
            {
                _countBossParries++;
            }
            _lastBossState = bossState;

            // Detect fight restarts or conclusion
            if (_wasCombatActive && !isCombatActive)
            {
                string outcome = !_player.IsAlive ? "BossWon" : "PlayerWon";
                RecordSnapshot(outcome);

                if (_verboseLogging)
                {
                    Debug.Log($"[TelemetryManager] Game #{_currentGameId} concluded ({outcome}). Total samples: {_sampleCountThisGame}. Player Hits Taken: {_countPlayerHitsTaken}, Boss Hits Taken: {_countBossHitsTaken}.");
                }

                _wasCombatActive = false;
                TriggerExcelExport();
            }
            else if (!_wasCombatActive && isCombatActive)
            {
                // New attempt started after conclusion
                _currentGameId++;
                StartNewGame();
            }

            if (!isCombatActive)
            {
                return;
            }

            // Check if boss started a new attack
            AttackDefinition currentBossAttack = _boss.CurrentAttack;
            bool attackInitiated = _logOnBossAttack &&
                                   currentBossAttack != null &&
                                   currentBossAttack != _lastObservedBossAttack;

            if (attackInitiated)
            {
                _lastObservedBossAttack = currentBossAttack;
                RecordSnapshot("Fighting");
            }
            else if (currentBossAttack == null)
            {
                _lastObservedBossAttack = null;
            }

            // Periodic interval recording
            if (currentTime >= _nextSampleTime)
            {
                _nextSampleTime = currentTime + _sampleIntervalSeconds;
                if (!attackInitiated)
                {
                    RecordSnapshot("Fighting");
                }
            }
        }

        private void RecordSnapshot(string encounterStatus)
        {
            if (_writer == null)
            {
                return;
            }

            try
            {
                CultureInfo inv = CultureInfo.InvariantCulture;

                float currentTime = Mathf.Max(0f, Time.time - _matchStartTime);
                int frame = Time.frameCount;

                Vector3 playerPos = _player.transform.position;
                Vector3 bossPos = _boss.transform.position;
                float distance = Vector3.Distance(playerPos, bossPos);

                Vector3 toPlayer = (playerPos - bossPos);
                toPlayer.y = 0f;
                float angle = toPlayer.sqrMagnitude > 0.001f
                    ? Vector3.SignedAngle(_boss.transform.forward, toPlayer.normalized, Vector3.up)
                    : 0f;

                // Player vitals
                float playerHp = _player.Health != null ? _player.Health.Current : 0f;
                float playerHpPct = _player.Health != null ? _player.Health.Normalized : 0f;
                float playerSta = _player.Stamina != null ? _player.Stamina.Current : 0f;
                float playerStaPct = _player.Stamina != null ? _player.Stamina.Normalized : 0f;
                float playerPost = _player.Posture != null ? _player.Posture.Current : 0f;
                float playerPostPct = _player.Posture != null ? _player.Posture.Normalized : 0f;
                int playerGuard = _player.IsGuarding ? 1 : 0;
                int playerInvuln = _player.IsInvulnerable ? 1 : 0;
                string playerState = _player.CurrentStateName;

                // Boss vitals
                float bossHp = _boss.Health != null ? _boss.Health.Current : 0f;
                float bossHpPct = _boss.Health != null ? _boss.Health.Normalized : 0f;
                float bossPoise = _boss.Poise != null ? _boss.Poise.Current : 0f;
                float bossPoisePct = _boss.Poise != null ? _boss.Poise.Normalized : 0f;
                int bossPhase = _boss.PhaseIndex;
                string bossState = _boss.CurrentStateName;
                string bossAction = _boss.CurrentAttack != null ? _boss.CurrentAttack.DisplayName : "None";

                // 12 Behavior features
                AdaptationManager adaptation = _boss.GetAdaptation();
                BehaviorProfile profile = adaptation?.Profile;

                float fAttackFreq = profile != null ? profile.Get(BehaviorFeature.AttackFrequency).Value : 0f;
                float fHeavyRatio = profile != null ? profile.Get(BehaviorFeature.HeavyAttackRatio).Value : 0f;
                float fPostDodge = profile != null ? profile.Get(BehaviorFeature.PostDodgeAggression).Value : 0f;
                float fDashBias = profile != null ? profile.Get(BehaviorFeature.DashDirectionBias).Value : 0f;
                float fPrefDist = profile != null ? profile.Get(BehaviorFeature.PreferredDistance).Value : 0f;
                float fAggression = profile != null ? profile.Get(BehaviorFeature.Aggression).Value : 0f;
                float fWhiff = profile != null ? profile.Get(BehaviorFeature.WhiffRatio).Value : 0f;
                float fHeal = profile != null ? profile.Get(BehaviorFeature.HealFrequency).Value : 0f;
                float fDodgeTime = profile != null ? profile.Get(BehaviorFeature.DodgeTimingConsistency).Value : 0f;
                float fEvasion = profile != null ? profile.Get(BehaviorFeature.EvasionSuccess).Value : 0f;
                float fGuardRel = profile != null ? profile.Get(BehaviorFeature.GuardReliance).Value : 0f;
                float fDeflectSkill = profile != null ? profile.Get(BehaviorFeature.DeflectSkill).Value : 0f;

                // 10 Boss tuning parameters
                BossTuning tuning = _boss.Tuning;
                float tAttackDelay = tuning != null ? tuning.Get(BossTuningParameter.AttackDelay) : 0f;
                float tPrefRange = tuning != null ? tuning.Get(BossTuningParameter.PreferredRange) : 0f;
                float tParryChance = tuning != null ? tuning.Get(BossTuningParameter.ParryChance) : 0f;
                float tGapCloser = tuning != null ? tuning.Get(BossTuningParameter.GapCloserWeight) : 0f;
                float tDodgePred = tuning != null ? tuning.Get(BossTuningParameter.DodgePredictionStrength) : 0f;
                float tAggression = tuning != null ? tuning.Get(BossTuningParameter.Aggression) : 0f;
                float tComboExt = tuning != null ? tuning.Get(BossTuningParameter.ComboExtensionChance) : 0f;
                float tFeint = tuning != null ? tuning.Get(BossTuningParameter.FeintChance) : 0f;
                float tAreaDenial = tuning != null ? tuning.Get(BossTuningParameter.AreaDenialWeight) : 0f;
                float tUnblockable = tuning != null ? tuning.Get(BossTuningParameter.UnblockableWeight) : 0f;

                string line = string.Format(inv,
                    "{0},{1:F2},{2},{3},{4:F2},{5:F1}," +
                    "{6},{7:F1},{8:F3},{9:F1},{10:F3},{11:F1},{12:F3},{13},{14}," +
                    "{15},{16},{17:F1},{18:F3},{19:F1},{20:F3},{21}," +
                    "{22:F3},{23:F3},{24:F3},{25:F3},{26:F3},{27:F3},{28:F3},{29:F3},{30:F3},{31:F3},{32:F3},{33:F3}," +
                    "{34:F3},{35:F2},{36:F3},{37:F3},{38:F3},{39:F3},{40:F3},{41:F3},{42:F3},{43:F3}," +
                    "{44},{45},{46},{47},{48},{49},{50},{51}",
                    _currentGameId,
                    currentTime,
                    frame,
                    encounterStatus,
                    distance,
                    angle,
                    playerState,
                    playerHp,
                    playerHpPct,
                    playerSta,
                    playerStaPct,
                    playerPost,
                    playerPostPct,
                    playerGuard,
                    playerInvuln,
                    bossState,
                    bossAction,
                    bossHp,
                    bossHpPct,
                    bossPoise,
                    bossPoisePct,
                    bossPhase,
                    fAttackFreq,
                    fHeavyRatio,
                    fPostDodge,
                    fDashBias,
                    fPrefDist,
                    fAggression,
                    fWhiff,
                    fHeal,
                    fDodgeTime,
                    fEvasion,
                    fGuardRel,
                    fDeflectSkill,
                    tAttackDelay,
                    tPrefRange,
                    tParryChance,
                    tGapCloser,
                    tDodgePred,
                    tAggression,
                    tComboExt,
                    tFeint,
                    tAreaDenial,
                    tUnblockable,
                    _countPlayerAttacks,
                    _countPlayerDodges,
                    _countPlayerDeflects,
                    _countPlayerBlocks,
                    _countPlayerHitsTaken,
                    _countBossAttacks,
                    _countBossParries,
                    _countBossHitsTaken);

                _writer.WriteLine(line);
                _writer.Flush();
                _sampleCountThisGame++;

                if (_verboseLogging && (_sampleCountThisGame == 1 || _sampleCountThisGame % 5 == 0 || encounterStatus != "Fighting"))
                {
                    Debug.Log($"[TelemetryManager] CSV row written! Game #{_currentGameId} [Sample #{_sampleCountThisGame}]: Status={encounterStatus}, PlayerHP={playerHp:F0}, BossHP={bossHp:F0}, Dist={distance:F1}m");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TelemetryManager] Failed to write row: {ex.Message}");
            }
        }

        private void CloseWriter()
        {
            if (_writer != null)
            {
                try
                {
                    _writer.Flush();
                    _writer.Dispose();
                }
                catch
                {
                    // Ignore during shutdown
                }
                _writer = null;
                TriggerExcelExport();
            }
        }

        private void TriggerExcelExport()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string scriptPath = Path.Combine(projectRoot, "export_excel.py");
                if (File.Exists(scriptPath))
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "python",
                        Arguments = $"\"{scriptPath}\"",
                        WorkingDirectory = projectRoot,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    System.Diagnostics.Process.Start(startInfo);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TelemetryManager] Excel export note: {ex.Message}");
            }
        }
    }
}
