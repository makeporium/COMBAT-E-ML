# Deep Comparison: adaptive-boss-arena vs. SKILL ISSUE

---

## TL;DR

These two projects solve the **same surface-level problem** ("adaptive boss in a Sekiro-style game") but through **completely different technical approaches**. The existing repo is actually listed as **Reference #1** in your own synopsis. Here's everything you need to know.

---

## Part 1 — What IS the AI in adaptive-boss-arena?

> Spoiler: there is **zero ML**. No sklearn, no model file, no training data, no Python. It's a hand-crafted statistics engine disguised as "adaptive AI."

### The full pipeline (traced through source code)

```
Combat Events (attacks, dodges, heals, deflects…)
        │
        ▼
  BehaviorTracker.cs   ← runs EVERY FRAME
  (EWMA smoothed stats: attack rate, separation, approach tendency)
  (DirectionHistogram: dash directions with exponential decay)
        │
        ▼
   CombatMemory.cs     ← ring buffer of last 256 events
   (raw counts: PlayerAttacksThrown, BossAttacksEvaded, PlayerDeflects…)
        │  (every few seconds, not per frame)
        ▼
  PatternRecognizer.cs ← runs on INTERVAL (design choice, not optimization)
  (derives 12 BehaviorFeatures, each normalized 0→1 with a confidence score)
        │
        ▼
  CounterSelector.cs   ← weighted-random strategy selection
  (picks from CounterStrategy ScriptableObject assets)
        │
        ▼
  AdaptationManager.cs ← the pacing/cooldown authority
  (enforces: min gap between adaptations, slow ease-in, forget decay)
        │
        ▼
   BossTuning.cs       ← 10 float parameters the FSM reads
   (values lerp toward targets, then decay back to baseline)
        │
        ▼
  BossController.cs FSM ← actual boss behavior execution
```

### The 12 "AI features" it tracks

| Feature | How it's computed |
|---|---|
| `AttackFrequency` | EWMA of attacks/sec, normalized against a 2.5 atk/s ceiling |
| `HeavyAttackRatio` | `heavyAttacks / totalAttacks` |
| `PostDodgeAggression` | `attacksAfterDodge / dodgeOpportunities` |
| `DashDirectionBias` | Entropy of a direction histogram (predictability score) |
| `PreferredDistance` | EWMA of separation distance |
| `Aggression` | EWMA of approach tendency (closing vs retreating) |
| `WhiffRatio` | `attacksWhiffed / totalAttacks` |
| `HealFrequency` | heals started / ceiling constant |
| `DodgeTimingConsistency` | `perfectDodges / dodgeOpportunities` |
| `EvasionSuccess` | `bossAttacksEvaded / bossAttacksResolved` |
| `GuardReliance` | `(deflects + blocks) / bossAttacksResolved` |
| `DeflectSkill` | `deflects / guardedAnswers` |

### What "adapts"?

Not the attack patterns. Not the FSM rules. The 10 **BossTuningParameter** floats:

| Parameter | Effect |
|---|---|
| `AttackDelay` | Delays attacks to punish post-dodge aggression |
| `PreferredRange` | Changes how close boss tries to stay |
| `ParryChance` | More parries vs heavy-attack spammers |
| `GapCloserWeight` | Prefers gap-closer attacks vs kiters |
| `DodgePredictionStrength` | Leads attacks toward a player's dodge bias |
| `Aggression` | Overall press-vs-reposition balance |
| `ComboExtensionChance` | Longer combos vs aggressive players |
| `FeintChance` | More feints vs deflect masters |
| `AreaDenialWeight` | Wide AoE attacks vs evasive players |
| `UnblockableWeight` | Unblockable attacks vs turtles |

### How data is "stored"

- **RAM only.** A `CircularBuffer<CombatEvent>` of capacity 256 in `CombatMemory.cs`
- No database, no file, no persistence
- **Resets completely when the fight restarts** (`AdaptationManager.Reset()` is called)
- The boss learns nothing between sessions — it's purely in-fight adaptation

### How "trained"?

It's **never trained**. The thresholds and weights are hand-authored by developers in Unity's Inspector as `CounterStrategy` ScriptableObject assets. A designer decides: "if `HeavyAttackRatio > 0.6` with confidence > 0.5, increase `ParryChance` to 0.8."

No gradient descent. No model fitting. No backward pass. Just `if feature > threshold → apply adjustment`.

### The "anti-cheat" architecture

The most sophisticated engineering in the repo is actually the **structural impossibility of cheating**:
- `AI` and `Learning` assemblies cannot reference `Player` or `InputSystem` at compile time
- Player state only reaches AI via `IObservablePlayer` (visible-only info)
- `DelayedPerceptionSource` adds ~140ms latency to all observations
- `ReactionProfile` clamps decisions to 220–420ms band with a `MissedOpportunityChance`

---

## Part 2 — What is YOUR project (SKILL ISSUE)?

### The actual ML pipeline

```
Unity Game (gameplay telemetry)
        │  (every ~5–10 seconds of combat, at boss decision points)
        ▼
TelemetryManager → CSV / database rows
(43 features per row: hp_percent, attack_frequency, dodge_rate, etc.)
        │
        ▼
Python Data Pipeline (offline)
  - Data cleaning
  - EDA
  - Feature engineering (aggression score, posture pressure ratio…)
  - Label: next_boss_action (10 classes)
        │
        ▼
sklearn Classifiers (trained offline)
  - Logistic Regression (baseline)
  - Decision Tree
  - Random Forest
  - Gradient Boosting
        │  (serialized with joblib/pickle)
        ▼
Python Inference API (Flask/FastAPI)
        │  (HTTP request per boss decision point)
        ▼
Unity queries API → uses predicted action
```

### Data storage

- **CSV files** or a **database** (your synopsis mentions PostgreSQL in the Chlorine reference)
- **~500–2,000 labeled rows** from recorded play sessions
- **Persisted to disk** — survives restarts, can be appended to
- Serialized model file (`.pkl` or `.joblib`)

### The "training problem" you correctly identified

You're right to worry. **500–2,000 rows is borderline for a multiclass problem with 10 classes and 43 features.**

- Minimum rule of thumb: ~10× samples per feature per class → you want `43 × 10 × 10 = 4,300` rows at minimum
- With class imbalance (Retreat and Feint will be rare), your model will struggle
- **But**: this is normal for academic/project ML — you handle it with:
  - Stratified k-fold cross-validation
  - Class weighting (`class_weight='balanced'` in sklearn)
  - SMOTE or oversampling for rare classes
  - Random Forest and Gradient Boosting are robust to small datasets vs. neural nets

The models don't need "a lot" of play to show the academic point. Even a 70% macro-F1 beating a 10% random baseline proves the concept. Your evaluation plan already covers this correctly.

---

## Part 3 — Similarity Score

| Dimension | adaptive-boss-arena | SKILL ISSUE | Similarity |
|---|---|---|---|
| Game style | Sekiro-style sword combat, 3D Unity | Sekiro-style sword combat, 3D Unity | 🔴 Very High |
| Boss phases | 3 phases, FSM | 3 phases implied | 🔴 High |
| Input features | 12 behavioral features | 43 features (superset) | 🟡 Overlapping |
| Data storage | RAM only, no persistence | CSV/DB, persistent | 🟢 Different |
| "AI" algorithm | EWMA + threshold rules (no ML) | Real supervised ML (RF, GB, etc.) | 🟢 Fundamentally Different |
| Training | Never trained, rules hand-authored | Offline trained on gameplay data | 🟢 Different |
| Inference | Pure C# in-process | Python API over HTTP | 🟢 Different |
| Adaptation mechanism | Tune 10 float parameters via lerp | Swap predicted action class entirely | 🟢 Different |

**Overall: ~30% similar** — same game genre/inspiration, completely different technical AI approach.

---

## Part 4 — The Honest Risk Assessment

### What adaptive-boss-arena does BETTER

| Aspect | Why it's better |
|---|---|
| No inference latency | Pure C# in-process; yours needs an HTTP call every 5–10s |
| Works from game start | No training data needed — adapts during the very first fight |
| Gradual, legible adaptation | Lerp easing means players can *see* the boss changing |
| Anti-cheat architecture | Structurally prevents cheating via assembly isolation |
| Explainable | Every decision is a readable threshold in an Inspector |

### What YOUR project does BETTER

| Aspect | Why it's better |
|---|---|
| Real ML | Actual classification models — this is the academic contribution |
| Data-driven | Boss behavior emerged from data, not designer rules |
| Evaluable | You have accuracy, F1, ROC-AUC — something to measure and compare |
| Generalizable | A retrained model could handle a totally different player archetype |
| Replayable data | Your telemetry persists — you can do proper train/test splits |

---

## Part 5 — Key Differences Summary

```
adaptive-boss-arena          SKILL ISSUE (your project)
─────────────────────        ──────────────────────────
EWMA / statistics            Random Forest / Gradient Boosting
In-memory ring buffer        CSV / PostgreSQL dataset
No training needed           ~500–2000 rows, offline training
Tune 10 float params         Predict 10 action classes directly
Pure C# in Unity             Python API + Unity client
Adapts in 1 fight            Needs play sessions to collect data
Never persists learning      Persists model between sessions
Hand-authored strategies     Data-derived decision boundaries
```

---

## Part 6 — Is your project "too similar"?

**No.** The existing repo is explicitly cited as a **reference** in your synopsis (Reference #1), which is the correct academic practice. The technical approach is fundamentally different:

- That project has **no sklearn**, **no training loop**, **no model file**, **no Python**
- Your project's entire contribution is the supervised ML pipeline, which doesn't exist there
- The game genre overlap is expected — you need a combat game to generate the telemetry

> [!IMPORTANT]
> If your evaluator asks why you chose this reference, the answer is: "It demonstrates the game architecture that generates the telemetry. The AI approach is entirely different — this project uses hand-authored threshold rules, while ours uses a trained supervised classifier."

---

## Part 7 — Practical Recommendations

1. **Collect data early.** Your 43 features need a working game first. Plan the `TelemetryManager` in Week 1.
2. **Handle class imbalance.** Rare actions (Feint, Retreat) will underperform. Use `class_weight='balanced'` and report per-class F1.
3. **Start with Random Forest.** It's robust to your dataset size and interpretable via feature importance — good for your EDA section.
4. **Mock the API first.** Build the Python inference server early; the HTTP latency (should be <50ms locally) matters for real-time.
5. **Compare against the "rule-based baseline"** (like adaptive-boss-arena's approach) if you want a strong result — it's a natural comparison.
6. **The EWMA features from the reference repo are your best feature engineering lead.** Features like `attack_frequency`, `dodge_timing_mean`, `reaction_time_mean` are exactly what their `BehaviorTracker` computes manually.
