# CSE3705: ARTIFICIAL INTELLIGENCE — END-TERM PROJECT BLUEPRINT
## Project Title: Adaptive 3D Boss Tactical Action Prediction (SKILL ISSUE)
### Academic Alignment & Feature Integration Guide for Course Code: CSE3705

---

## 1. Academic & Course Mapping Metadata

| Parameter | Course Detail |
| :--- | :--- |
| **Course Code & Title** | **CSE3705: Artificial Intelligence** (Credits: 3, Contact Hours: 2L + 1T) |
| **Academic Batch & Year** | Batch 2024, Academic Year 2026–27 |
| **Course Coordinators & Faculty** | Dr. Anusha Chhabra (Coordinator), Dr. Sukhandeep Kaur, Dr. Saurabh Mishra |
| **Project Weightage** | **40% of Total Course Grade** (10% Pre-submission + 30% End-Term Evaluation & Viva) |
| **Project Repository** | Public GitHub Repository (Mandatory Course Requirement) |

### Alignment with Official Course Outcomes (COs):
* **CO1 (Theories & Concepts of AI Systems)**: Formulating the boss combat agent using the **PEAS framework**, comparing **Reflex vs. Learning Agent architectures**, and implementing an **Expert System**.
* **CO2 (Heuristics, Search, Planning & Knowledge Representation)**: Formulating arena navigation with **A\* search**, framing combo execution as a **Constraint Satisfaction Problem (CSP)**, and modeling combat tactics via **First-Order Logic & Inference mechanisms**.
* **CO3 (Uncertainty, Intelligent Agents, Generative AI & Transformers)**: Handling combat state uncertainty using **Supervised ML Classifiers**, generating synthetic gameplay telemetry with **CTGAN / VAEs**, temporal move prediction using **Self-Attention Transformers**, and deploying a **Local LLM via LangChain** for tactical debriefs.

---

## 2. The Core Project Workflow: From Rule-Based to Machine Learning

```
┌────────────────────────────────────────────────────────────────────────────────┐
│ PHASE 1: THE RULE-BASED EXPERT SYSTEM (BASELINE)                               │
│ • Completely decouple the old heuristic habit-tracker.                         │
│ • Implement a clean, fixed Rule-Based AI (IF-THEN priority logic).             │
│ • Establishes the predictable, exploitable baseline.                           │
└──────────────────────────────────────┬─────────────────────────────────────────┘
                                       │
                                       ▼
┌────────────────────────────────────────────────────────────────────────────────┐
│ PHASE 2: TELEMETRY COLLECTION & PREPROCESSING                                  │
│ • Play matches against Rule-Based Boss; TelemetryManager logs decision windows.│
│ • Input Vector X (43 combat features) ──► Target Action y (8-10 move classes). │
│ • Clean, normalize, and balance dataset (SMOTE).                               │
└──────────────────────────────────────┬─────────────────────────────────────────┘
                                       │
                                       ▼
┌────────────────────────────────────────────────────────────────────────────────┐
│ PHASE 3: ML MODEL TRAINING & COMPARISON (Python Pipeline)                      │
│ • Train & tune: Logistic Regression, Decision Tree, Random Forest, XGBoost.   │
│ • Feature importance analysis (SHAP), Cross-validation, Confusion matrices.    │
│ • Export trained models to ONNX / Flask inference API.                         │
└──────────────────────────────────────┬─────────────────────────────────────────┘
                                       │
                                       ▼
┌────────────────────────────────────────────────────────────────────────────────┐
│ PHASE 4: ML BOSS INTEGRATION & COMPARATIVE BENCHMARKING                        │
│ • Swap rule execution for ML prediction inside Unity.                          │
│ • Run controlled benchmark matches (Rule-Based vs. ML Boss).                   │
│ • Compute empirical evaluation metrics (Win Rate, Duration, Entropy, Latency). │
└────────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Phase 1: Removing Advanced AI & Implementing Pure Rule-Based AI

### 3.1 Systems Removed from Current Codebase
The game currently contains an adaptation engine (`AdaptiveBossArena.Learning`). To establish a legitimate academic baseline, we strip player-habit learning:
1. **`AdaptationManager.cs`**: Disconnected from the game loop. The boss will no longer analyze player habits or adopt counter-strategies.
2. **`BehaviorTracker.cs` & `CombatMemory.cs`**: Stopped calculating rolling habit features (`feature_panic_roll_rate`, `feature_whiff_ratio`, etc.).
3. **`CounterStrategy` catalogue**: Removed dynamic parameter shifts (e.g., boss increasing unblockable attacks when player blocks).
4. **`BossTuning.cs`**: Fixed to constant baseline weights.

### 3.2 Design of the Pure Rule-Based Boss (Model-Based Reflex Agent)
The Rule-Based Boss uses a deterministic **Priority Rule-Chain** based strictly on current sensory input without memory:

```
[Boss Decision Window]
        │
        ├─► [Rule 1] IF Distance > 7.0m 
        │             └─► Action = GapCloser (Leap Strike)
        │
        ├─► [Rule 2] IF Player in 'PlayerAttackState' AND Distance <= 2.5m
        │             └─► Action = Parry/Deflect (Fixed 35% RNG roll)
        │
        ├─► [Rule 3] IF Player Posture < 30% AND Distance <= 3.0m
        │             └─► Action = HeavyAttack (Posture break punish)
        │
        ├─► [Rule 4] IF Boss Health < 40% AND Distance <= 2.0m AND CooldownReady
        │             └─► Action = AreaDenial (Ground Slam shockwave)
        │
        ├─► [Rule 5] IF Distance between 3.5m and 7.0m
        │             └─► Action = Approach / Reposition
        │
        └─► [Default] Close Range (Distance <= 2.5m)
                      └─► Action = LightAttackCombo
```

**Why this is crucial for the academic report:**  
Rule-based bosses suffer from **predictability**. A player who figures out the 7-meter threshold will intentionally bait leaps and punish the recovery every single time. This provides the concrete motivation for Machine Learning.

---

## 4. Phase 2: Comparative Evaluation Framework

To scientifically measure whether data-driven tactical selection produces measurably superior gameplay over hand-coded rules, we implement the full comparative evaluation matrix:

### 4.1 Evaluation Matrix (Rule-Based vs. ML Boss)

| Evaluation Metric | Rule-Based Boss | ML-Driven Boss | Academic & Formulation Detail |
| :--- | :---: | :---: | :--- |
| **Accuracy against Reference Labels** | N/A *(Source of rules)* | **Yes** | $\text{Accuracy} = \frac{\sum_{i=1}^N \mathbb{I}(y_i = \hat{y}_i)}{N}$ evaluated on an 80/20 train/test split. |
| **Precision, Recall, Macro-F1** | N/A | **Yes** | Calculated per action class to verify minority moves (e.g., Parries, Feints) are not neglected. |
| **Player Win Rate** | **Yes** | **Yes** | $\frac{\text{Player Victories}}{\text{Total Matches}} \times 100\%$. **Hypothesis**: Player win rate drops significantly against the ML boss. |
| **Average Fight Duration** | **Yes** | **Yes** | Mean survival time per encounter (seconds). Measures combat sustainability and pressure. |
| **Damage Dealt (Boss DPS & Total)** | **Yes** | **Yes** | Total hit points taken from the player and average damage dealt per combat second. |
| **Inference / Decision Latency** | **Yes** *(Rule execution time)* | **Yes** *(ML prediction latency)* | High-precision stopwatch timing ($\mu s$ vs. $ms$). Evaluates whether ML meets the real-time 60 FPS budget. |
| **Action Diversity (Shannon Entropy)** | **Yes** *(Low / skewed)* | **Yes** *(High / diverse)* | $H(A) = -\sum_{i=1}^K P(a_i) \log_2 P(a_i)$. Measures how unpredictable and tactically varied the boss moves are. |

---

## 5. Mapping CSE3705 Course Handout Modules into the Project

Here is how each unit in the **CSE3705 syllabus** can be explicitly demonstrated in your project report and viva:

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                        CSE3705 SYLLABUS TOPIC INTEGRATION                       │
├──────────────────────────────────────────────────┬──────────────────────────────┤
│ Course Module                                    │ Implemented Project Feature  │
├──────────────────────────────────────────────────┼──────────────────────────────┤
│ Topic 1 & 5: Intelligent Agents & PEAS Framework │ Formal PEAS + Agent Taxonomy │
│ Topic 2: Search & Problem Solving                │ A* Arena Navigation          │
│ Topic 3: Planning & CSP                          │ Combat Combo CSP Solver      │
│ Topic 4: Knowledge Representation & Logic        │ First-Order Tactical Rules   │
│ Topic 6 & 7: Generative AI (VAEs & GANs)         │ CTGAN Synthetic Telemetry    │
│ Topic 8: Transformer Architectures               │ Self-Attention Move Predictor│
│ Topic 9: LLMs, LangChain & Responsible AI        │ Boss Debrief via Local LLM   │
└──────────────────────────────────────────────────┴──────────────────────────────┘
```

---

### Feature 1: Intelligent Agents & PEAS Framework (Topics 1 & 5 — CO1)

In your report and viva, formally model both boss architectures using the Russell & Norvig framework:

#### 1. PEAS Specification:
* **Performance Measure**: Maximize boss damage dealt, maximize action entropy, minimize player win rate, maintain real-time responsiveness ($< 16.6\text{ ms}$).
* **Environment**: 3D circular arena with obstacles, continuous coordinates, non-stationary human opponent, partially observable (delayed perception).
* **Actuators**: Sword swings (Light, Heavy, Area Denial, Gap Closer), Defensive tactics (Parry, Guard, Dodge), Movement (Approach, Circle, Retreat).
* **Sensors**: Frame-rate combat telemetry, spatial vector distance, relative angle, vitals pools (HP, Stamina, Posture), player state observer.

#### 2. Agent Taxonomy Comparison:
* **Rule-Based Boss = Model-Based Reflex Agent**: Maintains internal state (cooldowns, health), but maps current state directly to actions via condition-action rules.
* **ML Boss = Learning Agent**: Contains a **Learning Element** (model training on telemetry), a **Critic** (evaluation metrics), a **Learning Goal** (action prediction accuracy), and a **Performance Element** (runtime inference engine).

---

### Feature 2: Search & Arena Pathfinding using A* (Topic 2 — CO1, CO2)

* **Current Implementation**: The boss currently uses standard continuous movement.
* **Course Feature to Add**: Integrate a discrete **A\* Search Grid** for boss movement during recovery:
  - Arena discretized into a $0.5\text{ m} \times 0.5\text{ m}$ grid.
  - **Ground Scars / Hazard Zones** produced by the boss's heavy attacks are treated as dynamic obstacle cost multipliers.
  - Evaluation function: $f(n) = g(n) + h(n)$, where $h(n)$ is the Euclidean distance to the optimal combat engagement bracket ($3.0\text{ m}$ from player).
  - Demonstrates **heuristic optimality** and path cost calculations required for CO2.

---

### Feature 3: Planning as a Constraint Satisfaction Problem (CSP) (Topic 3 — CO1, CO2)

Instead of hardcoding combos, model the boss's next 3-move sequence as a **Constraint Satisfaction Problem**:
* **Variables**: $\{X_1, X_2, X_3\}$ representing the 3 consecutive attacks in a combo.
* **Domain**: $D_i = \{\text{Light1}, \text{Light2}, \text{Heavy}, \text{Shockwave}, \text{Lunge}, \text{Parry}\}$.
* **Constraints**:
  1. $\sum \text{StaminaCost}(X_i) \le \text{CurrentBossStamina}$.
  2. $\text{Cooldown}(X_i) = 0$.
  3. **No consecutive heavy attacks**: $\text{If } X_i = \text{Heavy} \implies X_{i+1} \neq \text{Heavy}$ (prevents animation locking).
  4. **Reach constraint**: $\text{Reach}(X_1) \ge \text{CurrentPlayerDistance}$.
* Solved using **Backtracking Search with Forward Checking (MRV Heuristic)**.

---

### Feature 4: Knowledge Representation & Logical Reasoning (Topic 4 — CO2)

Formulate the boss's tactical decision rules in **First-Order Predicate Logic (FOL)** and demonstrate **Forward Chaining Inference**:

$$\forall p \ [ \text{Player}(p) \land \text{IsGuarding}(p) \land \text{DistanceLessThan}(p, 2.5) \implies \text{RecommendAction}(\text{UnblockableThrust}) ]$$
$$\forall p \ [ \text{Player}(p) \land \text{PostureBroken}(p) \implies \text{RecommendAction}(\text{HeavyExecution}) ]$$
$$\forall b \ [ \text{Boss}(b) \land \text{HealthCritical}(b) \land \text{EncounterPhase}(b, 2) \implies \text{ActivateSpecialAbility}(\text{CataclysmicWave}) ]$$

* Include truth tables, unification algorithms, and resolution refutation steps in your project documentation to fulfill Topic 4.

---

### Feature 5: Generative AI Data Augmentation with VAE / CTGAN (Topics 6 & 7 — CO1, CO3)

One of the largest hurdles in real gameplay ML is **data scarcity** (playing 50 matches yields only ~1,500 decision rows).
* **Course Feature to Add**: Implement a **Conditional Tabular GAN (CTGAN)** or **Variational Autoencoder (VAE)** in Python:
  - **Generator / Decoder**: Generates synthetic combat telemetry decision rows $(\tilde{X}, \tilde{y})$.
  - **Discriminator / Encoder**: Enforces feature correlation distribution matching real player gameplay.
  - **Class Imbalance Balancing**: Specifically synthesize rare combat events (e.g., player parrying unblockable moves).
  - Compare classifier performance: **Trained on Real Data Only vs. Trained on Real + CTGAN Synthetic Data**.

---

### Feature 6: Temporal Combat Prediction with Self-Attention Transformers (Topic 8 — CO1, CO3)

Human sword combat is inherently sequential—a single instant does not reflect a combo rhythm.
* **Course Feature to Add**: Build a lightweight **Time-Series Transformer Classifier** using PyTorch:
  - Input: A sequence of the last 5 combat states $\mathbf{X} = [S_{t-4}, S_{t-3}, S_{t-2}, S_{t-1}, S_t]$.
  - Architecture: Multi-Head Self-Attention layer:
    $$\text{Attention}(Q, K, V) = \text{softmax}\left(\frac{QK^T}{\sqrt{d_k}}\right)V$$
  - **Attention Heatmap Visualization**: Plot the attention matrix in your project report to show *which previous player frame* the boss paid attention to when deciding to parry!

---

### Feature 7: LLM Tactical Debrief & Responsible AI with LangChain (Topic 9 — CO3)

Add a modern Generative AI feature using a **Local LLM (Ollama running Mistral or Llama-3)** integrated via **LangChain**:
* **Post-Match Boss Tactical Debrief**:
  - After each match, `TelemetryManager` summarizes the fight stats (Player win rate, deflect rate, aggression ratio).
  - LangChain pipes these structured metrics into a prompt template:
    ```python
    prompt = PromptTemplate(
        input_variables=["player_win", "fight_time", "player_deflect_rate", "boss_damage"],
        template="You are Malakor, an ancient swordmaster boss. The player just fought you. "
                 "Outcome: {player_win}. Duration: {fight_time}s. Deflect Rate: {player_deflect_rate}%. "
                 "Write a 3-sentence in-character taunt and tactical critique explaining their combat weakness."
    )
    ```
  - Displays the generated response on the game's **End-Game Screen**.
* **Responsible & Ethical AI Analysis**:
  - Address **Algorithmic Fairness**: Ensuring the boss does not generate unbeatable impossible reactions.
  - Address **Data Privacy**: Confirming telemetry collection is anonymized and strictly local.

---

## 6. End-Term Project Evaluation & Viva Preparation

The course syllabus specifies that the project has a **40% total weightage** with both coding and viva components evaluated by the faculty (Dr. Anusha Chhabra, Dr. Sukhandeep Kaur, Dr. Saurabh Mishra).

### Top 5 Viva Questions & Model Answers:

1. **Q: Why is supervised action classification better than the rule-based approach?**  
   * **A**: Rule-based systems are static lookup tables; human players easily discover edge-case exploits (e.g. kiting at fixed range). Supervised ML models generalize across complex non-linear combinations of posture, distance, and health, producing dynamic counter-actions that adapt to varied player playstyles.

2. **Q: How did you evaluate whether the ML boss is genuinely better?**  
   * **A**: We measured both computational and gameplay metrics: offline accuracy and macro-F1 on test splits, inference latency, live player win rate, fight duration, and Shannon entropy ($H(A)$) to prove the ML boss exhibits higher action diversity and lower exploitability.

3. **Q: How does this project demonstrate Generative AI and Transformers (Topics 7 & 8)?**  
   * **A**: We addressed real-world data scarcity by using a CTGAN/VAE to synthesize balanced combat telemetry, and implemented a Multi-Head Self-Attention Transformer over a 5-frame temporal window to capture combo rhythms.

4. **Q: How does the PEAS model characterize this environment?**  
   * **A**: The environment is partially observable (delayed perception window), continuous (3D spatial coordinates), sequential (combos and posture recovery carry forward), and dynamic (the human player attacks in real time).

5. **Q: How did you handle class imbalance in your telemetry dataset?**  
   * **A**: Standard gameplay is skewed toward basic light attacks. We evaluated models using Macro-averaged F1 instead of naive accuracy, applied SMOTE data augmentation, and utilized cost-sensitive loss weighting (`class_weight='balanced'`).

---

## 7. Immediate Next Steps

1. **Rule-Based Boss Code Implementation**: Implement the standalone deterministic priority rule chain in `BossController.cs` and verify compilation.
2. **Collect 1,000+ Baseline Decision Rows**: Play 10–15 test matches to generate a rich telemetry training set in `gameplay_telemetry.csv`.
3. **Run Python ML Training**: Train the multiclass classification models and generate the confusion matrices and SHAP feature importance plots.
4. **Deploy Model to Unity**: Connect the ML predictor back into the boss decision loop.

