# SKILL ISSUE — Complete Project Roadmap

> **Using adaptive-boss-arena as the game core,  
> replacing its custom AI with Rule-Based logic,  
> then integrating supervised ML to drive the boss.**

---

## Table of Contents

1. [What We're Building — Big Picture](#1-what-were-building--big-picture)
2. [Phase 0 — Understand What's Already There](#2-phase-0--understand-whats-already-there)
3. [Phase 1 — Strip the Old AI, Keep the Game](#3-phase-1--strip-the-old-ai-keep-the-game)
4. [Phase 2 — Build the Rule-Based Boss](#4-phase-2--build-the-rule-based-boss)
5. [Phase 3 — Build the Telemetry Pipeline](#5-phase-3--build-the-telemetry-pipeline)
6. [Final Feature Variables](#6-final-feature-variables)
7. [Sample Dataset — 5 Gameplays](#7-sample-dataset--5-gameplays)
8. [Phase 4 — ML Concept Deep Dives](#8-phase-4--ml-concept-deep-dives)
   - [4A — Decision Tree](#4a--decision-tree)
   - [4B — Random Forest](#4b--random-forest)
   - [4C — Gradient Boosting](#4c--gradient-boosting)
   - [4D — Logistic Regression (Baseline)](#4d--logistic-regression-baseline)
9. [Phase 5 — Python ML Pipeline](#9-phase-5--python-ml-pipeline)
10. [Phase 6 — Inference API + Unity Integration](#10-phase-6--inference-api--unity-integration)
11. [Phase 7 — Evaluation](#11-phase-7--evaluation)
12. [Full Timeline](#12-full-timeline)
13. [Team Responsibilities](#13-team-responsibilities)

---

## 1. What We're Building — Big Picture

```
┌─────────────────────────────────────────────────────────────────┐
│                        FINAL ARCHITECTURE                       │
│                                                                 │
│  Unity Game (adaptive-boss-arena core, our rule-based boss)     │
│       │                                                         │
│       │  Every boss decision point (~5 sec)                     │
│       ▼                                                         │
│  TelemetryManager.cs  ─────────────────────┐                    │
│  (records 30 features to CSV / memory)     │                    │
│       │                                    │                    │
│       │  HTTP POST (JSON)                  │ Also writes rows   │
│       ▼                                    ▼                    │
│  Python Inference API          gameplay_log.csv                 │
│  (Flask, loads trained model)       (training data)            │
│       │                                                         │
│       │  HTTP response: predicted action                        │
│       ▼                                                         │
│  RuleBossBrain.cs                                               │
│  (executes the ML-predicted action via FSM)                     │
└─────────────────────────────────────────────────────────────────┘
```

**Two modes the project runs in:**

| Mode | What happens |
|---|---|
| **Data Collection** | Python API is off. Boss uses pure rule-based logic. Every boss decision is logged to CSV with the action it chose. This builds the training set. |
| **ML Mode** | Python API is on. Boss asks the model for the next action instead of using rules. We evaluate whether ML decisions look different / better. |

---

## 2. Phase 0 — Understand What's Already There

Before touching any code, every team member should know what to keep and what to delete.

### What the existing repo gives us (KEEP ALL OF THIS)

| System | Files | Why keep |
|---|---|---|
| Player controller | `PlayerController.cs`, `PlayerContext.cs`, `PlayerConfig.cs` | Full movement, dash, stamina, input buffering — done |
| Combat system | `Scripts/Combat/` | Hitboxes, combos, poise/stagger, perfect dodge, hit-stop |
| Health/Poise pools | Inside `Combat/Vitals/` | HP and posture bars work already |
| Boss FSM skeleton | `BossController.cs`, `AI/States/` | The state machine (Idle, Approach, Attack, Recover, Stagger, Dead) |
| Boss movement | `BossMotor.cs`, `BossPacing.cs` | Navigation, pathfinding around the arena |
| Attack definitions | `BossAttackSelector.cs`, `BossConfig.cs` | 3 phases of boss attacks defined as ScriptableObjects |
| Core interfaces | `Scripts/Core/` | `ITimeService`, `ICombatEventBus`, `IObservablePlayer` — the plumbing |
| Scene builder | `ArenaSceneBuilder.cs` (in Editor) | Generates the arena — rerun it, don't hand-edit scenes |
| UI | `Scripts/UI/` | Health bars, pause, UI plumbing |

### What we DELETE (the adaptive AI parts)

| What | Files | Why delete |
|---|---|---|
| Adaptive learning system | `Scripts/Learning/` (entire folder) | This is the EWMA/threshold engine we're replacing with ML |
| `AdaptationManager` wiring | Inside `BossController.cs` — `BuildLearning()` method + `_adaptation` field | This is where the Learning assembly is called |
| Adaptive tuning params | `BossTuningParameter` enum + `BossTuning.cs` | These float params belong to the old system |
| Counter strategies | ScriptableObjects under `ScriptableObjects/` that are `CounterStrategy` type | Designer-authored thresholds, not needed |
| Adaptation UI channel | `_adaptationChannel` in `BossController.cs` | The "boss has adapted!" HUD message |

> **The FSM itself stays.** We're not rewriting how the boss moves or attacks. We're only replacing *what decision the boss makes* when it's time to pick an action.

---

## 3. Phase 1 — Strip the Old AI, Keep the Game

### Step-by-step changes to BossController.cs

**1. Remove Learning assembly reference**

Open `AI/AdaptiveBossArena.AI.asmdef` and remove the reference to `AdaptiveBossArena.Learning`. This makes the entire Learning folder invisible to the AI — any remaining references will now be compile errors that guide cleanup.

**2. Delete the `BuildLearning()` method** (lines ~278–310 in BossController.cs)

```csharp
// DELETE this entire method:
private void BuildLearning(IRandomProvider random) { ... }
```

**3. Remove private fields that belonged to learning:**

```csharp
// DELETE these lines in the field declarations:
private AdaptationManager _adaptation;
private BossResolve _resolve;
private OverbalanceEvaluator _overbalance;
private float _adaptationRateScale;
```

**4. Remove `BuildLearning(random)` call from `Start()`**

**5. Remove the `_adaptationChannel` SerializeField and any `.Raise()` calls to it**

**6. In `Update()` / tick code**, remove any `_adaptation.Tick(...)` calls

**7. Simplify `BossAttackSelector`** — strip out `gapCloserBias`, `areaDenialBias`, `unblockableBias` from `ScoreCatalogue()`. Replace with pure range-based selection (simpler). The rule-based boss will use this.

**After deletion, the project should compile cleanly.** The boss will still move and attack — it just uses static probability weights now. That's Phase 2.

---

## 4. Phase 2 — Build the Rule-Based Boss

This is our "dumb boss" that generates training data. It must be **deterministic enough to produce diverse labeled data**, but **simple enough that the ML model can clearly do better**.

### Design: RuleBossBrain.cs (new file)

```
Called every ~5 seconds (boss decision interval)
         │
         │  Reads combat state snapshot
         ▼
  Distance check → {Close, Mid, Far}
         │
         ├─ Close → random pick from {LightAttack, HeavyAttack, Deflect}
         │          weighted 60/25/15
         │
         ├─ Mid   → random pick from {LightAttack, Chase, Dodge}
         │          weighted 50/30/20
         │
         └─ Far   → random pick from {Retreat, Chase, SpecialAttack}
                    weighted 40/40/20
```

**Additional rules layered on top (this is what makes it "rule-based"):**

```
IF boss_hp_percent < 0.3  → unlock SpecialAttack (weight ×2)
IF player_hp_percent < 0.2 → increase Aggression (LightAttack weight ×1.5)
IF boss_posture_percent > 0.8 → force Retreat or Dodge (posture guard)
IF player is in Heal animation → force Chase or LightAttack
IF last_3_actions were all attacks → insert Dodge or Retreat
```

### Why this rule-based approach produces good training data

- It's **varied** — probabilistic choices mean many different action sequences
- It's **labeled automatically** — whatever action the rule system picks becomes the `next_boss_action` label for that row
- It's **imperfect** — which means the ML model genuinely has room to do better
- It covers **all 8 action classes** consistently across plays

### Boss action classes (the ML target variable)

| ID | Action | Description |
|---|---|---|
| 0 | `LightAttack` | Fast low-damage swing |
| 1 | `HeavyAttack` | Slow high-damage/unblockable swing |
| 2 | `Deflect` | Parry the player's incoming attack |
| 3 | `Dodge` | Sidestep away |
| 4 | `Chase` | Close the distance gap |
| 5 | `Retreat` | Open distance, buy time |
| 6 | `CounterAttack` | Attack immediately after surviving a player hit |
| 7 | `SpecialAttack` | Phase-specific powerful move |

---

## 5. Phase 3 — Build the Telemetry Pipeline

### TelemetryManager.cs (new MonoBehaviour, attach to GameManager)

**When it fires:** Every time the boss makes a decision (every ~5 seconds, or on boss state transition).

**What it does:**
1. Reads 30 feature values from the current game state
2. Packages them as one row
3. In **DataCollection mode**: appends to `gameplay_log.csv` and also includes the boss action chosen (the label)
4. In **MLMode**: sends features as JSON to the Python inference API, waits for predicted action

```csharp
// Pseudocode structure
public class TelemetryManager : MonoBehaviour
{
    [SerializeField] private bool mlMode = false;
    [SerializeField] private string apiUrl = "http://localhost:5000/predict";
    
    private StreamWriter _csvWriter;
    private CombatSnapshot _snap; // rolling stats, reset each fight
    
    public void OnBossDecisionPoint(float currentTime)
    {
        TelemetryRow row = BuildRow(currentTime);
        
        if (mlMode)
            StartCoroutine(QueryMLApi(row));
        else
        {
            string label = GetRuleBasedAction().ToString();
            AppendRowToCSV(row, label);
        }
    }
    
    private TelemetryRow BuildRow(float t) { /* reads all 30 features */ }
}
```

### CombatSnapshot.cs (rolling statistics tracker)

This tracks the running numbers between decision points. Reset on fight start.

```csharp
public class CombatSnapshot
{
    // Counters (since last decision point, or since fight start)
    public int PlayerAttacksThrown;
    public int PlayerHeavyAttacks;
    public int PlayerDodges;
    public int PlayerDeflects;
    public int PlayerBlocks;
    public int BossAttacksLanded;
    public int BossAttacksEvaded;
    
    // Continuous
    public float CumulativeDistance;
    public int DistanceSamples;
    public float LastEventTime;
    
    // Phase tracking
    public int CurrentPhase;
    public int LastBossAction;       // action ID 0-7
    public int SecondLastBossAction;
    public int ThirdLastBossAction;
    
    // Methods
    public void Reset() { ... }
    public void OnPlayerAttack(bool isHeavy) { ... }
    public void OnPlayerDodge() { ... }
    public void OnBossAttackLanded() { ... }
    // etc.
}
```

---

## 6. Final Feature Variables

These are the **30 features** every row in our dataset will contain.  
Rationale for each is in the comments.

### Group A — Current Health State (4 features)

| Column Name | Type | Range | What it measures |
|---|---|---|---|
| `player_hp_pct` | float | 0.0–1.0 | Player current HP normalized. Boss should be more aggressive when player is low. |
| `boss_hp_pct` | float | 0.0–1.0 | Boss current HP normalized. Boss may retreat or risk-take when low. |
| `player_posture_pct` | float | 0.0–1.0 | Player posture fill. High = player near stagger, good time to attack. |
| `boss_posture_pct` | float | 0.0–1.0 | Boss posture fill. High = boss needs to retreat or deflect. |

### Group B — Positioning (3 features)

| Column Name | Type | Range | What it measures |
|---|---|---|---|
| `player_boss_distance` | float | 0.0–20.0 | World-units distance right now. The single most important situational feature. |
| `distance_trend` | float | -1.0–1.0 | Negative = closing, Positive = retreating. Is player moving toward or away? |
| `close_range_ratio` | float | 0.0–1.0 | Fraction of last 10 distance samples that were ≤ 3 units (melee range). |

### Group C — Player Attack Patterns (6 features)

| Column Name | Type | Range | What it measures |
|---|---|---|---|
| `attack_count_recent` | int | 0–20 | Number of player attacks in last 15 seconds. Aggression measure. |
| `heavy_attack_ratio` | float | 0.0–1.0 | Heavy attacks / total attacks. High = parry opportunity. |
| `whiff_ratio` | float | 0.0–1.0 | Attacks that missed / total attacks. High = player is panicking or poor aim. |
| `avg_combo_length` | float | 1.0–5.0 | Average consecutive attacks before a pause. |
| `attack_after_dodge_ratio` | float | 0.0–1.0 | How often player attacks right after dodging. Classic habit to counter. |
| `attack_interval_avg` | float | 0.5–10.0 | Average seconds between player attacks. |

### Group D — Player Defensive Patterns (5 features)

| Column Name | Type | Range | What it measures |
|---|---|---|---|
| `dodge_count_recent` | int | 0–15 | Player dodges in last 15 seconds. |
| `perfect_dodge_ratio` | float | 0.0–1.0 | Perfect dodges / total dodges. Measures dodge skill level. |
| `deflect_count_recent` | int | 0–10 | Player deflects/parries in last 15 seconds. |
| `block_count_recent` | int | 0–10 | Player blocks (non-perfect deflects) in last 15 seconds. |
| `guard_reliance` | float | 0.0–1.0 | (deflects + blocks) / boss attacks. Are they a "turtle" player? |

### Group E — Outcome Stats (4 features)

| Column Name | Type | Range | What it measures |
|---|---|---|---|
| `hit_rate_player` | float | 0.0–1.0 | Player attacks that landed / total player attacks. |
| `hit_rate_boss` | float | 0.0–1.0 | Boss attacks that landed / total boss attacks. |
| `damage_dealt_recent` | float | 0.0–200.0 | Total damage player dealt in last 15 seconds. |
| `damage_received_recent` | float | 0.0–200.0 | Total damage boss dealt to player in last 15 seconds. |

### Group F — Boss State (4 features)

| Column Name | Type | Range | What it measures |
|---|---|---|---|
| `boss_phase` | int | 1–3 | Current boss phase (1=calm, 2=enraged, 3=last stand). |
| `time_since_boss_last_attack` | float | 0.0–30.0 | Seconds since boss last attacked. Cooldown awareness. |
| `boss_last_action` | int | 0–7 | The action the boss just took (encoded as int). |
| `boss_second_last_action` | int | 0–7 | Two actions ago. Pattern context. |

### Group G — Derived / Computed Features (4 features)

These are computed from the raw columns above, not directly measured. They pack more meaning per column.

| Column Name | Formula | What it means |
|---|---|---|
| `aggression_score` | `attack_count_recent/15 × (1 - player_boss_distance/20)` | Combined attack rate + proximity. The most aggressive players score high. |
| `defensive_score` | `(dodge_count_recent/10 × 0.5) + (guard_reliance × 0.5)` | How defensively the player is playing overall. |
| `posture_pressure_ratio` | `player_posture_pct - boss_posture_pct` | Who is closer to staggering. Positive = player is in trouble. |
| `fight_duration_pct` | `elapsed_time / max_expected_fight_time` | How far into the fight we are. Early vs late game context. |

### Target Variable

| Column Name | Type | Values |
|---|---|---|
| `next_boss_action` | int (categorical) | 0=LightAttack, 1=HeavyAttack, 2=Deflect, 3=Dodge, 4=Chase, 5=Retreat, 6=CounterAttack, 7=SpecialAttack |

**Total: 30 features + 1 target = 31 columns per row.**

---

## 7. Sample Dataset — 5 Gameplays

Each row below represents one boss decision point snapshot.  
Five different player archetypes produce different data patterns.

> **Notation:** Each block = one complete fight, ~8–12 decision points captured.  
> For brevity, only 4 rows per fight are shown (real collection = ~12–15 per fight).

---

### CSV Header

```
row_id, player_hp_pct, boss_hp_pct, player_posture_pct, boss_posture_pct, player_boss_distance, distance_trend, close_range_ratio, attack_count_recent, heavy_attack_ratio, whiff_ratio, avg_combo_length, attack_after_dodge_ratio, attack_interval_avg, dodge_count_recent, perfect_dodge_ratio, deflect_count_recent, block_count_recent, guard_reliance, hit_rate_player, hit_rate_boss, damage_dealt_recent, damage_received_recent, boss_phase, time_since_boss_last_attack, boss_last_action, boss_second_last_action, aggression_score, defensive_score, posture_pressure_ratio, fight_duration_pct, next_boss_action
```

---

### Gameplay 1 — "The Aggressive Rusher" (never dodges, always attacks)

```
1,  0.95, 1.00, 0.20, 0.05, 2.1, -0.8, 0.90, 12, 0.25, 0.10, 3.2, 0.70, 1.2,  1, 0.00, 0, 0, 0.03, 0.75, 0.45, 88,  34, 1, 3.1, 4, 5, 0.87, 0.05, 0.15, 0.12, 0
2,  0.78, 0.85, 0.35, 0.10, 1.8, -0.5, 0.95, 15, 0.33, 0.15, 3.8, 0.80, 1.0,  2, 0.00, 0, 0, 0.05, 0.68, 0.50, 110, 48, 1, 1.5, 0, 0, 0.91, 0.07, 0.25, 0.25, 2
3,  0.62, 0.70, 0.55, 0.30, 2.4, -0.3, 0.80, 18, 0.28, 0.18, 4.0, 0.75, 0.9,  3, 0.00, 0, 1, 0.06, 0.70, 0.48, 130, 62, 2, 2.2, 2, 0, 0.85, 0.08, 0.25, 0.45, 1
4,  0.45, 0.52, 0.70, 0.45, 1.9, -0.6, 0.92, 20, 0.40, 0.20, 4.2, 0.85, 0.8,  2, 0.00, 0, 0, 0.04, 0.65, 0.52, 145, 78, 2, 4.5, 1, 2, 0.93, 0.06, 0.25, 0.65, 7
```

*Pattern: Very close range (distance ~2), high attack count, low dodge, low deflect. Boss response trend: more HeavyAttacks (1) and SpecialAttacks (7) to punish reckless aggression. Eventually Deflect (2) to punish the predictable rhythm.*

---

### Gameplay 2 — "The Kiter" (stays far, pokes and runs)

```
5,  0.90, 0.95, 0.10, 0.02, 12.5, 0.7, 0.05, 4, 0.00, 0.30, 1.5, 0.10, 5.5, 3, 0.33, 0, 0, 0.02, 0.40, 0.20, 28, 12, 1, 6.2, 5, 5, 0.11, 0.20, 0.08, 0.10, 4
6,  0.88, 0.88, 0.08, 0.05, 14.2, 0.9, 0.02, 3, 0.00, 0.50, 1.2, 0.05, 6.1, 4, 0.25, 0, 0, 0.02, 0.35, 0.18, 20, 10, 1, 5.8, 4, 5, 0.08, 0.22, 0.03, 0.22, 4
7,  0.85, 0.80, 0.12, 0.08, 11.8, 0.5, 0.08, 5, 0.20, 0.40, 1.8, 0.12, 5.0, 5, 0.20, 0, 0, 0.03, 0.38, 0.22, 32, 15, 1, 4.1, 4, 4, 0.13, 0.25, 0.04, 0.35, 7
8,  0.82, 0.72, 0.15, 0.12, 10.5, 0.4, 0.10, 6, 0.17, 0.33, 2.0, 0.15, 4.5, 6, 0.33, 0, 0, 0.03, 0.42, 0.25, 38, 18, 2, 7.0, 7, 4, 0.17, 0.27, 0.03, 0.50, 4
```

*Pattern: Far range (distance 10–14), low attack count, boss mostly chasing (4). Boss eventually uses SpecialAttack (7) which has gap-closing lunge. Notice fight takes longer (fight_duration_pct rises slowly).*

---

### Gameplay 3 — "The Deflect Master" (parries everything perfectly)

```
9,  0.98, 1.00, 0.05, 0.02, 3.2, -0.2, 0.70, 8, 0.12, 0.05, 2.5, 0.30, 2.2, 4, 0.75, 6, 0, 0.75, 0.62, 0.30, 55, 8,  1, 2.8, 0, 3, 0.42, 0.57, 0.03, 0.15, 0
10, 0.97, 0.88, 0.06, 0.15, 2.8, -0.3, 0.75, 9, 0.11, 0.06, 2.8, 0.33, 2.0, 5, 0.80, 8, 0, 0.80, 0.60, 0.28, 62, 10, 1, 1.9, 0, 0, 0.45, 0.60, -0.09, 0.28, 6
11, 0.96, 0.72, 0.07, 0.25, 3.0, -0.1, 0.72, 10, 0.10, 0.05, 3.0, 0.30, 1.9, 5, 0.80, 9, 0, 0.82, 0.62, 0.25, 72, 11, 1, 3.3, 6, 0, 0.47, 0.62, -0.18, 0.42, 3
12, 0.95, 0.58, 0.08, 0.40, 3.1, 0.1, 0.68, 11, 0.09, 0.06, 3.1, 0.28, 1.8, 6, 0.83, 10, 0, 0.83, 0.63, 0.22, 80, 12, 2, 4.0, 3, 6, 0.48, 0.64, -0.32, 0.60, 1
```

*Pattern: Very high `perfect_dodge_ratio` (0.75–0.83), very high `deflect_count_recent`, low `guard_reliance` (deflects ≠ blocks). Boss eventually picks Deflect (3) to parry counter-attacks, then HeavyAttack (1) — unblockable — to break the deflect rhythm. Notice boss_posture rises (boss getting parried a lot).*

---

### Gameplay 4 — "The Turtle" (blocks everything, attacks rarely)

```
13, 0.80, 0.95, 0.20, 0.05, 4.5, 0.3, 0.40, 2, 0.00, 0.20, 1.0, 0.10, 8.5, 0, 0.00, 0, 8, 0.95, 0.30, 0.30, 15, 20, 1, 8.1, 5, 5, 0.08, 0.67, 0.15, 0.18, 1
14, 0.79, 0.88, 0.22, 0.08, 5.0, 0.4, 0.35, 1, 0.00, 0.50, 1.0, 0.00, 10.0, 0, 0.00, 0, 9, 0.96, 0.25, 0.35, 8,  22, 1, 9.5, 1, 5, 0.05, 0.70, 0.14, 0.32, 1
15, 0.78, 0.80, 0.25, 0.12, 4.8, 0.2, 0.38, 2, 0.00, 0.40, 1.2, 0.05, 9.0, 0, 0.00, 0, 11, 0.97, 0.28, 0.38, 12, 25, 1, 6.2, 1, 1, 0.06, 0.72, 0.13, 0.48, 1
16, 0.77, 0.72, 0.28, 0.18, 5.2, 0.3, 0.32, 3, 0.33, 0.33, 1.5, 0.00, 8.0, 1, 0.00, 0, 12, 0.97, 0.32, 0.40, 18, 28, 2, 7.0, 1, 1, 0.10, 0.73, 0.10, 0.65, 1
```

*Pattern: Extremely high `block_count_recent` and `guard_reliance` (~0.97), zero deflects, very low attack frequency. Boss responds with HeavyAttack (1) = unblockable attacks to break the turtle. `attack_interval_avg` very high — player barely attacks.*

---

### Gameplay 5 — "The Balanced Fighter" (mixes everything, above-average skill)

```
17, 0.85, 0.90, 0.15, 0.10, 5.5, -0.1, 0.50, 7, 0.28, 0.15, 2.5, 0.43, 2.8, 3, 0.33, 2, 2, 0.44, 0.58, 0.40, 48, 30, 1, 3.5, 0, 3, 0.38, 0.38, 0.05, 0.15, 0
18, 0.80, 0.78, 0.20, 0.22, 4.8, -0.2, 0.55, 8, 0.25, 0.13, 2.8, 0.38, 2.6, 4, 0.50, 3, 2, 0.45, 0.60, 0.38, 58, 36, 1, 2.8, 0, 0, 0.42, 0.41, -0.02, 0.30, 6
19, 0.72, 0.65, 0.28, 0.32, 5.0, 0.0, 0.52, 9, 0.22, 0.12, 2.8, 0.44, 2.5, 5, 0.60, 4, 2, 0.45, 0.61, 0.36, 68, 42, 2, 4.1, 6, 0, 0.44, 0.44, -0.04, 0.50, 0
20, 0.65, 0.52, 0.35, 0.40, 4.5, -0.3, 0.58, 10, 0.20, 0.10, 3.0, 0.50, 2.3, 5, 0.60, 5, 2, 0.47, 0.63, 0.35, 78, 48, 2, 3.2, 0, 6, 0.47, 0.46, -0.05, 0.68, 0
```

*Pattern: Balanced all-around. Mixed dodge/deflect, moderate attack frequency, mid-range combat. Boss uses LightAttack (0) and CounterAttack (6) mostly — the "correct" answer for a balanced fighter is to pressure back without overcommitting.*

---

## 8. Phase 4 — ML Concept Deep Dives

---

### 4A — Decision Tree

#### What is it?

A Decision Tree is an algorithm that learns a series of **if/else questions** from training data and builds them into a tree structure.

Think of it like a flow chart the computer builds **automatically** from the data:

```
                    [player_boss_distance ≤ 3.5?]
                         /              \
                       YES               NO
                        |                 |
          [boss_posture_pct ≥ 0.7?]   [close_range_ratio ≤ 0.2?]
              /          \                /           \
           YES            NO           YES             NO
            |              |            |               |
          Retreat      LightAttack    Chase         CounterAttack
```

Each **node** is a question about one feature. Each **branch** is the yes/no answer. Each **leaf** (end node) is a predicted action. The tree is built by finding the question that best separates the classes at each step.

#### The math behind it (simplified)

The algorithm uses **Gini impurity** (or entropy) to score every possible split:

```
Gini = 1 - Σ(pᵢ²)

where pᵢ = fraction of each class in a group
```

A perfect group (all one class) has Gini = 0.  
A perfectly mixed group (equal all classes) has Gini close to 1.

The algorithm tries every feature × every threshold, picks the split that reduces Gini the most. Repeat at each child node until the tree is pure or hits a depth limit.

#### Why are we using it?

1. **Interpretable** — you can print the tree and explain exactly why it predicted HeavyAttack
2. **Fast** — prediction is just traversing a tree, microseconds per call
3. **No scaling needed** — unlike Logistic Regression, raw numbers work fine
4. **Baseline for understanding** — before using complex models, see if simple rules the tree learns match what you'd hand-code
5. **Feature importance** — tells you which of your 30 features actually matter

#### Why not use Neural Networks instead?

| Neural Network | Decision Tree |
|---|---|
| Needs thousands of rows (we have 500–2000) | Works well with small datasets |
| Black box — can't explain decisions | Fully explainable |
| Takes minutes/hours to train | Trains in milliseconds |
| Great for images/audio | Not necessary for tabular game data |
| Hard to debug | Easy to inspect and fix |

For academic projects with small tabular data, Decision Trees are always the right starting point. Neural networks would be overkill and would overfit badly on our dataset size.

#### Hyperparameters to tune

| Parameter | What it controls | Recommended range |
|---|---|---|
| `max_depth` | How many questions deep the tree goes | 3–15 (start at 5) |
| `min_samples_split` | Minimum rows needed to split a node | 2–20 |
| `min_samples_leaf` | Minimum rows in a leaf | 1–10 |
| `criterion` | Impurity measure: 'gini' or 'entropy' | try both |

#### Overfitting problem

A Decision Tree without depth limits will perfectly memorize training data (100% training accuracy) but fail on new data. Control this with `max_depth`. Always compare train accuracy vs. test accuracy.

---

### 4B — Random Forest

#### What is it?

Random Forest is an **ensemble** of many Decision Trees, each trained on a random subset of the data and a random subset of features. The final prediction is the **majority vote** of all trees.

```
Training data (1500 rows, 30 features)
         │
         ├──► Tree 1 (trained on 1050 random rows, 17 random features)  →  predicts: LightAttack
         ├──► Tree 2 (trained on 1050 random rows, 20 random features)  →  predicts: LightAttack
         ├──► Tree 3 (trained on 1050 random rows, 15 random features)  →  predicts: Dodge
         ├──► Tree 4 (trained on 1050 random rows, 18 random features)  →  predicts: LightAttack
         └──► Tree 5 ... (100 trees total by default)
         
Final vote: LightAttack wins (3 out of 4) → predict LightAttack
```

The key ideas:
- **Bagging (Bootstrap Aggregating):** Each tree sees ~63% of the training data (randomly sampled with replacement). The rest (~37%) becomes an out-of-bag validation set — free cross-validation.
- **Feature randomness:** At each split, only a random subset of features is considered. This prevents all trees from learning the same thing.

#### Why are we using it?

1. **Much better accuracy than a single Decision Tree** — averaging many imperfect trees cancels out their individual errors
2. **Resistant to overfitting** — because each tree sees different data + features
3. **Works great on small-to-medium tabular datasets** (exactly our case: 500–2000 rows)
4. **Feature importance** — averaging importance across all trees gives more reliable rankings
5. **Handles class imbalance** better than a single tree (use `class_weight='balanced'`)
6. **No feature scaling needed** — raw numbers fine
7. **Very hard to tune wrong** — default `n_estimators=100` already works well

#### Why not use SVM (Support Vector Machine) instead?

| Random Forest | SVM |
|---|---|
| Works great on small tabular data ✓ | Also good on small data |
| Fast training ✓ | Slow training on 30 features |
| Gives feature importance ✓ | No feature importance |
| Handles multi-class naturally ✓ | Needs one-vs-all wrapper |
| Intuitive to explain ✓ | Kernel trick is hard to explain academically |
| No feature scaling required ✓ | Requires feature scaling |

SVM is powerful but harder to justify academically and harder to tune for multiclass. Random Forest wins for our use case.

#### Hyperparameters to tune

| Parameter | What it controls | Recommended |
|---|---|---|
| `n_estimators` | Number of trees | 100–500 |
| `max_depth` | Maximum depth of each tree | None, 10, 20 |
| `max_features` | Features considered at each split | 'sqrt' (default), 'log2' |
| `min_samples_leaf` | Minimum samples in leaf | 1–5 |
| `class_weight` | Handle class imbalance | 'balanced' |

#### Out-of-Bag Score

Random Forest gives you a **free validation score** without needing a separate validation set:

```python
rf = RandomForestClassifier(n_estimators=100, oob_score=True)
rf.fit(X_train, y_train)
print(rf.oob_score_)  # free unbiased accuracy estimate
```

Each tree is validated on the rows it never saw during training (~37% of data). No k-fold needed just to get a rough accuracy.

---

### 4C — Gradient Boosting

#### What is it?

Gradient Boosting builds trees **sequentially**, where each new tree **corrects the mistakes** of all previous trees. Unlike Random Forest (parallel trees), Gradient Boosting is an iterative process.

```
Step 1: Fit Tree 1 to the raw labels
         → makes some errors (predicted Chase, actual was LightAttack)

Step 2: Fit Tree 2 to the *residuals* (the errors from Tree 1)
         → corrects cases where Tree 1 was wrong

Step 3: Fit Tree 3 to the residuals of (Tree 1 + Tree 2)
         → corrects the remaining harder cases

...continue for n_estimators iterations...

Final prediction = Tree1_prediction + Tree2_correction + Tree3_correction + ...
```

The "gradient" part: at each step, the next tree is fitted to the **negative gradient of the loss function** — for classification this is log-loss. This is analogous to gradient descent in neural networks, but on tree residuals.

#### The learning rate

Gradient Boosting adds each new tree at a fraction of its full contribution:

```
prediction += learning_rate × new_tree_prediction
```

Low `learning_rate` (e.g., 0.05) + many trees = better generalization but slower training.  
High `learning_rate` (e.g., 0.3) + few trees = faster but may overfit.

#### We'll use XGBoost or sklearn's GradientBoostingClassifier

```python
from sklearn.ensemble import GradientBoostingClassifier
# or
from xgboost import XGBClassifier  # faster, same concept
```

#### Why are we using it?

1. **Usually the most accurate model on tabular data** — wins most Kaggle tabular competitions
2. **Handles feature interactions automatically** — when the boss should attack depends on both distance AND player hp, GBM finds this
3. **Built-in regularization** — `subsample`, `max_depth` prevent overfitting
4. **Works well with class imbalance** — `scale_pos_weight` or `class_weight`

#### Why not use Neural Networks (Deep Learning)?

| Gradient Boosting | Neural Network (MLP) |
|---|---|
| Excellent on small tabular data (our case ✓) | Needs large data to shine |
| Trains in seconds ✓ | Can take minutes–hours |
| Explainable feature importance ✓ | Black box |
| Deterministic and reproducible ✓ | Training depends on random init |
| 1–5 hyperparameters to tune ✓ | Dozens of architecture choices |
| Industry standard for tabular ML ✓ | Overkill for 30 features |

#### Why is it better than Random Forest?

In most benchmarks on tabular data, Gradient Boosting outperforms Random Forest by 2–5% accuracy because it focuses learning effort on hard examples rather than averaging equally. **Random Forest is better when:**
- Data is very noisy
- You need fast real-time training (RF trains faster)
- You need an out-of-bag score without k-fold

**Gradient Boosting is better when:**
- You have the time to tune hyperparameters
- You want maximum accuracy
- Your classes have complex interactions

We use **both** and compare.

#### Hyperparameters to tune

| Parameter | What it controls | Recommended |
|---|---|---|
| `n_estimators` | Number of trees | 100–500 |
| `learning_rate` | Step size per tree | 0.01–0.3 |
| `max_depth` | Tree depth | 3–8 (shallower than RF) |
| `subsample` | Fraction of data per tree | 0.7–1.0 |
| `min_samples_leaf` | Min samples in leaf | 1–10 |

---

### 4D — Logistic Regression (Baseline)

#### What is it?

Logistic Regression finds a **linear decision boundary** in feature space. For each class, it learns a set of weights:

```
P(action = LightAttack) = sigmoid(w₁×player_boss_distance + w₂×attack_count_recent + ... + b)
```

The `sigmoid` function squashes output to 0–1 (probability). For multiclass, we use **softmax** instead.

#### Why use it?

- **Baseline** — if Random Forest gets 80% accuracy and Logistic Regression gets 55%, that's a clear demonstration that nonlinear models are better for this problem
- **Fast** — trains in milliseconds, serves predictions in microseconds  
- **Interpretable** — weights directly show how each feature pulls toward each action
- **Required for academic comparison** — your synopsis explicitly lists it

#### The weakness

Logistic Regression assumes a **linear relationship** between features and the prediction. Combat dynamics are highly nonlinear (distance matters differently depending on boss phase, HP, etc.). Expect it to score 50–65% — acceptable as a baseline, clearly worse than tree methods.

---

## 9. Phase 5 — Python ML Pipeline

### Project structure

```
skill_issue_ml/
├── data/
│   ├── gameplay_log.csv          ← raw telemetry from Unity
│   └── processed/
│       ├── train.csv
│       └── test.csv
├── notebooks/
│   ├── 01_EDA.ipynb              ← exploratory data analysis
│   ├── 02_Feature_Engineering.ipynb
│   └── 03_Model_Comparison.ipynb
├── models/
│   ├── decision_tree.pkl
│   ├── random_forest.pkl
│   ├── gradient_boosting.pkl
│   └── logistic_regression.pkl
├── api/
│   ├── app.py                    ← Flask inference server
│   └── predict.py                ← loads model, runs inference
├── train.py                      ← full training pipeline script
└── requirements.txt
```

### Step 1 — EDA (Exploratory Data Analysis)

```python
import pandas as pd
import seaborn as sns
import matplotlib.pyplot as plt

df = pd.read_csv("data/gameplay_log.csv")

# Class distribution — crucial to see imbalance
df["next_boss_action"].value_counts().plot(kind="bar")

# Correlation heatmap — see which features matter
sns.heatmap(df.corr(), annot=False, cmap="coolwarm")

# Per-action feature distributions
for feature in ["player_boss_distance", "aggression_score", "guard_reliance"]:
    df.boxplot(column=feature, by="next_boss_action")
```

### Step 2 — Preprocessing

```python
from sklearn.model_selection import train_test_split
from sklearn.preprocessing import StandardScaler

X = df.drop("next_boss_action", axis=1)
y = df["next_boss_action"]

# Stratified split — ensures all 8 action classes appear in test set
X_train, X_test, y_train, y_test = train_test_split(
    X, y, test_size=0.2, random_state=42, stratify=y
)

# Scaling (only needed for Logistic Regression — trees don't need it)
scaler = StandardScaler()
X_train_scaled = scaler.fit_transform(X_train)
X_test_scaled = scaler.transform(X_test)
```

### Step 3 — Train all four models

```python
from sklearn.tree import DecisionTreeClassifier
from sklearn.ensemble import RandomForestClassifier, GradientBoostingClassifier
from sklearn.linear_model import LogisticRegression
from sklearn.model_selection import StratifiedKFold, cross_val_score
import joblib

models = {
    "Decision Tree": DecisionTreeClassifier(max_depth=8, class_weight="balanced", random_state=42),
    "Random Forest": RandomForestClassifier(n_estimators=200, class_weight="balanced", random_state=42, n_jobs=-1),
    "Gradient Boosting": GradientBoostingClassifier(n_estimators=200, learning_rate=0.1, max_depth=5, random_state=42),
    "Logistic Regression": LogisticRegression(max_iter=1000, class_weight="balanced", random_state=42),
}

cv = StratifiedKFold(n_splits=5, shuffle=True, random_state=42)

for name, model in models.items():
    X_input = X_train_scaled if name == "Logistic Regression" else X_train
    
    cv_scores = cross_val_score(model, X_input, y_train, cv=cv, scoring="f1_macro")
    print(f"{name}: CV Macro-F1 = {cv_scores.mean():.3f} ± {cv_scores.std():.3f}")
    
    model.fit(X_input, y_train)
    joblib.dump(model, f"models/{name.lower().replace(' ', '_')}.pkl")
```

### Step 4 — Evaluate

```python
from sklearn.metrics import classification_report, confusion_matrix, ConfusionMatrixDisplay

action_names = ["LightAttack","HeavyAttack","Deflect","Dodge","Chase","Retreat","CounterAttack","SpecialAttack"]

best_model = joblib.load("models/random_forest.pkl")
y_pred = best_model.predict(X_test)

print(classification_report(y_test, y_pred, target_names=action_names))

cm = confusion_matrix(y_test, y_pred)
ConfusionMatrixDisplay(cm, display_labels=action_names).plot()
```

### Step 5 — Feature Importance

```python
import numpy as np

rf = joblib.load("models/random_forest.pkl")
importances = pd.Series(rf.feature_importances_, index=X_train.columns)
importances.nlargest(15).plot(kind="barh", title="Top 15 Features")
```

---

## 10. Phase 6 — Inference API + Unity Integration

### Python API (api/app.py)

```python
from flask import Flask, request, jsonify
import joblib
import numpy as np

app = Flask(__name__)

# Load the best model at startup
model = joblib.load("models/random_forest.pkl")
FEATURE_COLUMNS = [
    "player_hp_pct","boss_hp_pct","player_posture_pct","boss_posture_pct",
    "player_boss_distance","distance_trend","close_range_ratio",
    "attack_count_recent","heavy_attack_ratio","whiff_ratio",
    "avg_combo_length","attack_after_dodge_ratio","attack_interval_avg",
    "dodge_count_recent","perfect_dodge_ratio","deflect_count_recent",
    "block_count_recent","guard_reliance","hit_rate_player","hit_rate_boss",
    "damage_dealt_recent","damage_received_recent","boss_phase",
    "time_since_boss_last_attack","boss_last_action","boss_second_last_action",
    "aggression_score","defensive_score","posture_pressure_ratio","fight_duration_pct"
]

@app.route("/predict", methods=["POST"])
def predict():
    data = request.json
    features = np.array([[data[col] for col in FEATURE_COLUMNS]])
    action_id = int(model.predict(features)[0])
    probabilities = model.predict_proba(features)[0].tolist()
    return jsonify({"action_id": action_id, "probabilities": probabilities})

@app.route("/health", methods=["GET"])
def health():
    return jsonify({"status": "ok"})

if __name__ == "__main__":
    app.run(host="0.0.0.0", port=5000, debug=False)
```

Run with: `python api/app.py`

### Unity side — MLBossClient.cs (new file)

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class MLBossClient : MonoBehaviour
{
    [SerializeField] private string apiUrl = "http://localhost:5000/predict";
    [SerializeField] private float requestTimeoutSeconds = 0.5f;
    
    public void RequestNextAction(TelemetryRow row, System.Action<int> onResult)
    {
        StartCoroutine(SendRequest(row, onResult));
    }
    
    private IEnumerator SendRequest(TelemetryRow row, System.Action<int> onResult)
    {
        string json = JsonUtility.ToJson(row);
        var request = new UnityWebRequest(apiUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = Mathf.CeilToInt(requestTimeoutSeconds);
        
        yield return request.SendWebRequest();
        
        if (request.result == UnityWebRequest.Result.Success)
        {
            var response = JsonUtility.FromJson<MLResponse>(request.downloadHandler.text);
            onResult(response.action_id);
        }
        else
        {
            // Fallback to rule-based if API unreachable
            Debug.LogWarning("[ML] API unreachable — falling back to rule-based.");
            onResult(GetRuleBasedFallback());
        }
    }
    
    [System.Serializable]
    private class MLResponse { public int action_id; }
    
    private int GetRuleBasedFallback() => 0; // LightAttack as safe default
}
```

### Flow in RuleBossBrain.cs (modified for ML mode)

```csharp
public void OnDecisionPoint()
{
    TelemetryRow row = _telemetry.BuildCurrentRow();
    
    if (_mlMode && _mlClient != null)
    {
        _mlClient.RequestNextAction(row, ExecuteAction);
    }
    else
    {
        int ruleAction = ComputeRuleBasedAction(row);
        _telemetry.LogRow(row, ruleAction);  // save to CSV
        ExecuteAction(ruleAction);
    }
}
```

### Latency budget

The boss makes a decision every ~5 seconds. The API call should complete in under 200ms. On a local machine (localhost), Flask serving a Random Forest prediction takes **5–20ms**. Well within budget.

---

## 11. Phase 7 — Evaluation

### Metrics to report

```python
from sklearn.metrics import (
    accuracy_score, f1_score, precision_score, recall_score,
    roc_auc_score, classification_report, confusion_matrix
)

# Overall accuracy
acc = accuracy_score(y_test, y_pred)

# Macro F1 — treats every class equally (important since some actions are rare)
f1 = f1_score(y_test, y_pred, average="macro")

# Per-class F1 — see which actions the model struggles with
report = classification_report(y_test, y_pred, target_names=action_names)

# Multiclass ROC-AUC (optional — needs predict_proba)
auc = roc_auc_score(y_test, model.predict_proba(X_test), multi_class="ovr")
```

### Expected results (rough estimates)

| Model | Expected Accuracy | Expected Macro F1 |
|---|---|---|
| Random baseline | ~12.5% (1/8 classes) | ~0.125 |
| Logistic Regression | 50–60% | 0.40–0.55 |
| Decision Tree | 60–72% | 0.52–0.65 |
| Random Forest | 72–82% | 0.65–0.78 |
| Gradient Boosting | 75–85% | 0.68–0.80 |

Even 72% with macro F1 of 0.65 is a **strong academic result** — it's 6× better than random and proves the ML approach works.

### Inference latency test

```python
import time

model = joblib.load("models/random_forest.pkl")
sample = X_test.iloc[:1]

start = time.perf_counter()
for _ in range(1000):
    model.predict(sample)
elapsed = (time.perf_counter() - start) / 1000 * 1000  # ms per prediction

print(f"Average inference latency: {elapsed:.2f} ms")
# Expected: 0.5–5ms for Random Forest, 100 trees
```

---

## 12. Full Timeline

```
Week 1 — Game Setup
├── Fork adaptive-boss-arena to your team repo
├── Delete Scripts/Learning/ folder
├── Remove BuildLearning() from BossController.cs
├── Verify game compiles and boss still moves/attacks
└── Run ArenaSceneBuilder to regenerate scene

Week 2 — Rule-Based Boss + Telemetry
├── Write RuleBossBrain.cs (3 distance zones × simple probability rules)
├── Write CombatSnapshot.cs (rolling counters)
├── Write TelemetryManager.cs (decision-point firing, CSV writer)
├── Define TelemetryRow struct with all 30 features
└── Play 10 test fights → verify CSV has reasonable rows

Week 3 — Data Collection Sprint
├── Collect 20–30 full fight recordings
│   (team members play different styles: rusher, kiter, turtle, deflect master)
├── Aim for 1500–2000 rows total
├── Run data quality checks (no nulls, correct ranges, class distribution)
└── Commit gameplay_log.csv to repo

Week 4 — Python ML Pipeline
├── 01_EDA.ipynb: distribution plots, class balance, correlation heatmap
├── 02_Feature_Engineering.ipynb: compute derived features, check importances
├── 03_Model_Comparison.ipynb: train all 4 models, compare CV scores
├── Confusion matrix analysis per model
└── Select best model (likely Random Forest or Gradient Boosting)

Week 5 — API + Unity Integration
├── Write Flask inference API (app.py)
├── Write MLBossClient.cs in Unity
├── Test round-trip: Unity → API → back to Unity in <200ms
├── Implement ML mode toggle in TelemetryManager
└── Record 5 fights in ML mode, observe behavior differences

Week 6 — Final Evaluation + Report
├── Run full evaluation: accuracy, macro F1, per-class F1, confusion matrix
├── Measure inference latency (must be <200ms)
├── Compare ML boss behavior vs rule-based across same player styles
├── Write report sections (EDA, model comparison, results)
└── Record demo video
```

---

## 13. Team Responsibilities

| Person | Weeks | Responsibilities |
|---|---|---|
| **Ayush** | 1–2, 5–6 | Unity project setup, delete old AI, wire TelemetryManager, MLBossClient.cs, final evaluation |
| **Siddharth** | 2–3, 6 | RuleBossBrain.cs implementation, CombatSnapshot.cs, EDA notebook, confusion matrix analysis |
| **Swayam** | 3–4 | Data collection sessions, feature engineering notebook, Random Forest + Gradient Boosting training, API integration |
| **Shivam** | 2, 4–5 | Boss behavior scripting (rule-based tuning), Logistic Regression + Decision Tree modeling, inference latency testing |

---

## Key Decisions Summary

| Decision | Choice | Reason |
|---|---|---|
| Number of features | 30 (trimmed from 43) | 43 had redundant features; 30 balances richness vs. dataset size |
| Target classes | 8 | Matches FSM states that already exist in the game |
| Primary model | Random Forest | Best accuracy/interpretability tradeoff for our dataset size |
| Comparison models | Decision Tree, GBM, LR | Required by synopsis; demonstrates range of ML approaches |
| Inference architecture | Python Flask API | Unity ML-Agents alternative was overkill for a classifier |
| Data collection | Rule-based boss logs its own actions | Automatic labeling — no human annotation needed |
| Train/test split | 80/20 stratified | Ensures rare classes (Feint, SpecialAttack) appear in test set |

---

*Document Version 1.0 — SKILL ISSUE Project, Team: Ayush, Siddharth, Swayam, Shivam*
