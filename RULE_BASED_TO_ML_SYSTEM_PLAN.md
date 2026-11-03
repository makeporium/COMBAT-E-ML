# RULE-BASED TO ML BOSSFIGHT ARCHITECTURE & EVALUATION PLAN
## Academic Project: Adaptive 3D Boss Tactical Action Prediction (SKILL ISSUE)

---

## 1. Executive Summary & Core Objective

The goal of this project is to replace traditional hand-coded game AI with a **Machine Learning Action Prediction Model** that selects the boss's next tactical combat action based on real-time combat telemetry.

### The 3-Step Project Plan:
```
┌────────────────────────────────────────────────────────────────────────┐
│ STEP 1: RULE-BASED BASELINE (No Learning)                              │
│ • Strip the existing complex adaptation heuristic.                     │
│ • Implement a clean, fixed Rule-Based AI (IF-THEN decision tree).      │
│ • Play games to collect natural combat telemetry to CSV.               │
└──────────────────────────────────┬─────────────────────────────────────┘
                                   │
                                   ▼
┌────────────────────────────────────────────────────────────────────────┐
│ STEP 2: ML TRAINING PIPELINE (Python)                                  │
│ • Train Supervised Multiclass Classifiers (Logistic Regression,        │
│   Decision Tree, Random Forest, Gradient Boosting).                    │
│ • Optimize hyperparameters, evaluate cross-validation metrics.         │
│ • Export best model for live inference (ONNX / Flask API).             │
└──────────────────────────────────┬─────────────────────────────────────┘
                                   │
                                   ▼
┌────────────────────────────────────────────────────────────────────────┐
│ STEP 3: COMPARATIVE EVALUATION (Rule-Based vs ML Boss)                 │
│ • Replace the rule logic with the ML prediction brain in Unity.        │
│ • Benchmark Rule-Based Boss vs ML Boss across gameplay & computational │
│   metrics (Win Rate, Fight Duration, Action Diversity, Latency).       │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Phase 1: Removing Advanced AI & Implementing Pure Rule-Based AI

### 2.1 Systems to Remove / Disconnect from Current Codebase
The existing codebase contains a heuristic player-habit analyzer (`AdaptiveBossArena.Learning`). To establish a proper academic baseline, we remove player-habit adaptation:
1. **`AdaptationManager.cs`**: Disconnect the background strategy analyzer that continuously recalibrates tuning parameters.
2. **`BehaviorTracker.cs` & `CombatMemory.cs`**: Stop computing running rolling averages of player habits (e.g. `feature_panic_roll_rate`, `feature_whiff_ratio`).
3. **`CounterStrategy` catalogue**: Remove dynamic boss parameter shifts (e.g. boss arbitrarily raising `unblockable_weight` when player guards).
4. **`BossTuning.cs` dynamic updates**: Lock the boss's physical stats (speed, parry chance, attack cooldowns) to constant baseline values.

### 2.2 Architecture of the New Pure Rule-Based Boss
The Rule-Based Boss operates as a **Deterministic Finite State Machine (FSM) / Priority Rule Chain**. It makes tactical decisions based strictly on **immediate spatial and physical conditions**, completely unaware of player history or habits:

```
[Boss Decision Point]
        │
        ├─► Rule 1: IF Player Distance > 7.0m 
        │            └─► Action = GapCloser (Leap / Sprint Slash)
        │
        ├─► Rule 2: IF Player is in 'PlayerAttackState' AND Distance <= 2.5m
        │            └─► Action = Deflect/Parry (Fixed 30% chance based on RNG, not learning)
        │
        ├─► Rule 3: IF Player Posture < 30% AND Distance <= 3.0m
        │            └─► Action = HeavyAttack (Attempt posture-break punish)
        │
        ├─► Rule 4: IF Boss Health < 40% AND Distance <= 2.0m AND CooldownReady
        │            └─► Action = AreaDenial (Ground Slam shockwave)
        │
        ├─► Rule 5: IF Distance between 3.0m and 7.0m
        │            └─► Action = Chase / Approach
        │
        └─► Default (Close Range <= 2.5m)
                     └─► Action = LightAttackCombo
```

**Why this is crucial for your project:**  
Rule-based bosses are predictable. Once a human player notices that standing at 8 meters always provokes a leap, or that swinging a light sword is never parried, the human player easily exploits the boss. This establishes the exact problem statement your ML model is designed to solve.

---

## 3. Phase 2: Telemetry Data Collection Pipeline

While the Rule-Based Boss is active, the game records every single **decision window** into `gameplay_telemetry.csv`.

### 3.1 Feature Vector (Inputs $X$)
At each decision point (every ~1.0s or when the boss initiates a move), the pipeline snapshots:
* **Spatial Metrics (2)**: `distance_to_boss`, `relative_angle_deg`.
* **Player Vitals & State (7)**: `player_hp_pct`, `player_stamina_pct`, `player_posture_pct`, `player_state` (encoded), `player_is_guarding`, `player_is_invulnerable`, `player_speed`.
* **Boss Vitals & State (5)**: `boss_hp_pct`, `boss_poise_pct`, `boss_phase`, `boss_state`, `boss_cooldown_ready`.
* **Recent Combat Dynamics (6)**: `time_since_last_hit`, `damage_dealt_recent`, `damage_received_recent`, `attack_count_recent`, `dodge_count_recent`, `current_combo_index`.

### 3.2 Target Label (Output $y$)
* **`next_boss_action`** (Multiclass label with $K = 8$ to $10$ distinct classes):
  1. `LightAttackCombo`
  2. `HeavyAttack`
  3. `GapCloser` (Leap / Lunge)
  4. `AreaDenial` (Ground Slam shockwave)
  5. `Deflect/Parry`
  6. `Guard/Block`
  7. `Dodge/Evade`
  8. `Retreat/Reposition`
  9. `Unblockable` (Perilous thrust)
  10. `Feint`

Target dataset size: **1,000 to 2,500 labeled decision rows** across 15–30 gameplay matches.

---

## 4. Phase 3: Machine Learning Model Development

We formulate the tactical decision problem as a **Supervised Multiclass Classification** task:
$$\hat{y} = f(X; \theta)$$
where $X \in \mathbb{R}^{d}$ is the combat state vector and $\hat{y} \in \{1, 2, \dots, K\}$ is the predicted boss action.

### Comparison of the 4 Proposed Algorithms:

| Algorithm | Model Type | Why We Use It | Academic Role in Project |
| :--- | :--- | :--- | :--- |
| **Logistic Regression** | Linear / Generalized Linear Model (GLM) | Fast, highly regularized baseline using Softmax: $P(y=k\|x) = \frac{e^{w_k^T x}}{\sum_j e^{w_j^T x}}$ | Serves as the minimal complexity benchmark. |
| **Decision Tree (CART)** | Non-linear Tree Partitioning | Splitting nodes via Gini Impurity $I_G = 1 - \sum p_i^2$ or Entropy. Directly mimics human game-designer rules. | Provides high interpretability; can be visualized and compared 1:1 against the original hand-written rules. |
| **Random Forest** | Bagging Ensemble of De-correlated Trees | Trains hundreds of decision trees with Bootstrap Aggregation & Random Feature Subspaces ($m = \sqrt{p}$). | Mitigates overfitting on small telemetry datasets; robust against noisy player movements. |
| **Gradient Boosting (XGBoost / LightGBM)** | Boosting Ensemble of Residual Learners | Trains sequential weak trees to minimize multiclass Cross-Entropy loss: $F_m(x) = F_{m-1}(x) + \gamma_m h_m(x)$. | Expected to achieve the highest predictive accuracy and F1 score on complex non-linear combat states. |

---

## 5. Phase 4: Integrating the ML Model into Unity

Once the ML models are trained in Python:
1. **Option A (Lightweight Microservice)**:
   - A local Python Flask / FastAPI server loads `best_model.joblib`.
   - Unity sends HTTP POST request: `{"distance": 3.2, "player_hp": 80.0, ...}`.
   - Server returns JSON: `{"action": "HeavyAttack", "latency_ms": 1.8}`.
2. **Option B (Embedded ONNX Runtime)**:
   - Export model to Open Neural Network Exchange (`model.onnx`).
   - Run inference directly inside Unity using Unity Sentis or `Microsoft.ML.OnnxRuntime` with **zero network latency** (< 0.2ms).

---

## 6. Phase 5: The Comparative Evaluation Framework

To scientifically evaluate the project, we benchmark the **Rule-Based Boss** against the **ML Boss** across both offline machine learning metrics and live combat gameplay metrics:

### 6.1 Evaluation Matrix (As Required)

| Evaluation Metric | Rule-Based Boss | ML-Driven Boss | Formulation / Measurement Method |
| :--- | :---: | :---: | :--- |
| **Accuracy against Reference Labels** | N/A (Source of baseline rules) | **Yes** | $\text{Accuracy} = \frac{\sum_{i=1}^N \mathbb{I}(y_i = \hat{y}_i)}{N}$ evaluated on a held-out test split (80/20 train/test). |
| **Precision, Recall, Macro-F1** | N/A | **Yes** | Evaluated per attack class: $F_1 = 2 \cdot \frac{\text{Precision} \cdot \text{Recall}}{\text{Precision} + \text{Recall}}$ to handle minority classes (e.g. rare unblockable attacks). |
| **Player Win Rate** | **Yes** | **Yes** | $\frac{\text{Player Wins}}{\text{Total Matches Played}} \times 100\%$. Hypothesis: Win rate drops against ML Boss because it exploits player habits. |
| **Average Fight Duration** | **Yes** | **Yes** | Total fight duration from match start to death/victory (seconds). |
| **Damage Dealt (Boss DPS & Total)** | **Yes** | **Yes** | Total damage inflicted on player and average damage dealt per second. |
| **Inference / Decision Latency** | **Yes** *(Rule execution time)* | **Yes** *(ML prediction time)* | Time in microseconds ($\mu s$) or milliseconds ($ms$) required to compute an action decision. |
| **Action Diversity (Shannon Entropy)** | **Yes** | **Yes** | $H(A) = -\sum_{i=1}^K P(a_i) \log_2 P(a_i)$. Measures whether the boss spams one attack or uses a diverse tactical arsenal. |

---

## 7. AI / ML Course Features to Add (Academic Handout Modules)

To ensure this project receives top marks in your university AI/ML course, we can integrate the standard curriculum requirements into the project:

### 1. Data Cleaning & Exploratory Data Analysis (EDA)
* **Missing Value & Outlier Imputation**: Filtering anomaly telemetry rows caused by frame drops or pauses.
* **Correlation Heatmap (Pearson / Spearman)**: Quantifying dependencies between player distance, posture, and attack choices.
* **Feature Distribution Plots (Seaborn/Matplotlib)**: Visualizing skewness in player behavior (e.g., histograms of player reaction time).

### 2. Feature Engineering & Selection
* **Derived Tactical Features**:
  - `Aggression_Score`: $\frac{\text{Forward Movement} \times \text{Attack Frequency}}{\text{Distance}}$.
  - `Posture_Pressure_Ratio`: $\frac{\text{Player Posture Damage}}{\text{Boss Posture Damage}}$.
* **Feature Importance & Selection**:
  - Mutual Information Score ($I(X; Y)$) to eliminate irrelevant spatial features.
  - Principal Component Analysis (PCA) for 2D visualization of combat state clusters.

### 3. Handling Class Imbalance
* Gameplay telemetry often has 60% Light Attacks and only 5% Deflects or Unblockables.
* **Techniques applied**:
  - **SMOTE** (Synthetic Minority Over-sampling Technique).
  - Cost-Sensitive Learning (`class_weight='balanced'`).

### 4. Hyperparameter Tuning & Cross-Validation
* **Stratified 5-Fold Cross Validation** to ensure temporal combat consistency across splits.
* **GridSearchCV & RandomizedSearchCV**:
  - Tuning Tree Depth, Min Samples Split, Number of Estimators, Learning Rate ($\eta$), and Regularization ($\lambda$).

### 5. Model Explainability & Interpretability (XAI)
* **SHAP (SHapley Additive exPlanations)**: Showing *why* the ML model decided to throw an Area Denial shockwave (e.g. "+35% probability because Player Distance was 8.2m and Player HP was <20%").
* **Confusion Matrix Analysis**: Identifying where the model confuses similar moves (e.g. Light Attack vs Heavy Attack wind-up).

### 6. Reinforcement Learning Extension (Bonus Course Topic)
* If your course covers Reinforcement Learning:
  - Formulate combat as a **Markov Decision Process (MDP)**:
    - State $S_t$: `(PlayerHP, BossHP, Distance, PlayerState)`.
    - Action $A_t$: `{Light, Heavy, Deflect, Evade, GapCloser}`.
    - Reward $R_t$: $+10$ for dealing damage, $-10$ for receiving damage, $+50$ for posture break, $+100$ for defeating player.
  - Train a **Q-Learning / Deep Q-Network (DQN)** agent to benchmark against the Supervised ML model.

---

> [!NOTE]
> **To personalize this plan to your exact syllabus:**  
> Please share or paste the **handout / syllabus text of your AI course**. We will map your professor's exact assignment units, evaluation rubrics, and preferred tools directly into the project structure.

