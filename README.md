# COMBAT-E-ML: Adaptive 3D Boss Tactical Combat with Rule-Based & ML Architecture
## Academic End-Term Project — CSE3705: Artificial Intelligence
### B.Tech Computer Science and Engineering | Batch 2024 | Academic Year 2026–27

---

## 1. Project Overview

**COMBAT-E-ML** is a 3D real-time combat research arena built in Unity (C#) and Python. It investigates whether a **Supervised Machine Learning Agent** trained on real player combat telemetry can outperform and out-adapt a deterministic **Rule-Based Expert System (Reflex Agent)**.

The project directly satisfies the 40% End-Term Project requirement for **CSE3705: Artificial Intelligence** (under Dr. Anusha Chhabra, Dr. Sukhandeep Kaur, and Dr. Saurabh Mishra), mapping theoretical paradigms (intelligent agents, state-space search, CSP planning, generative data synthesis, and responsible AI) into a functional game prototype.

---

## 2. System Architecture & Dual-Agent Workflow

```
                                  [Player Combat Actions]
                                             │
                                             ▼
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ UNITY 3D COMBAT ARENA                                                                  │
│                                                                                        │
│   ┌───────────────────────────────┐           ┌────────────────────────────────────┐   │
│   │   AGENT 1: RULE-BASED BOSS    │           │    TELEMETRY COLLECTION PIPELINE   │   │
│   │ • Model-Based Reflex Agent    │           │ • 52 exact combat features logged  │   │
│   │ • Priority IF-THEN Rules      │ ────────► │ • Microsecond decision latency     │   │
│   │ • Deterministic baseline      │           │ • Dual-layer hit detection         │   │
│   │ • Zero player-habit learning  │           │ • Real-time export (.csv & .xlsx)  │   │
│   └───────────────────────────────┘           └─────────────────┬──────────────────┘   │
└─────────────────────────────────────────────────────────────────┼──────────────────────┘
                                                                  │
                                                                  ▼
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ PYTHON MACHINE LEARNING PIPELINE                                                       │
│                                                                                        │
│   ┌───────────────────────────────┐           ┌────────────────────────────────────┐   │
│   │      MODEL TRAINING SUITE     │           │   ACADEMIC BENCHMARK & EVALUATION  │   │
│   │ • Logistic Regression         │           │ • Accuracy against reference labels│   │
│   │ • Decision Tree Classifier    │ ────────► │ • Precision, Recall, Macro-F1      │   │
│   │ • Random Forest (Ensemble)    │           │ • Player Win Rate & Fight Duration │   │
│   │ • Histogram Gradient Boosting │           │ • Shannon Entropy H(A) Diversity   │   │
│   └───────────────────────────────┘           └────────────────────────────────────┘   │
│                                   │                                                    │
│                                   ▼                                                    │
│                   [Serialized ONNX / Model Artifact]                                   │
└───────────────────────────────────┬────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ UNITY 3D COMBAT ARENA                                                                  │
│   ┌───────────────────────────────┐                                                    │
│   │     AGENT 2: ML-DRIVEN BOSS   │                                                    │
│   │ • Supervised Learning Agent   │                                                    │
│   │ • Evaluates telemetry state   │                                                    │
│   │ • Dynamic counter-prediction  │                                                    │
│   └───────────────────────────────┘                                                    │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Academic Evaluation Matrix

This project empirically calculates all target evaluation metrics before and after transitioning from Rule-Based to Machine Learning:

| Evaluation Metric | Rule-Based Boss (Baseline) | ML-Driven Boss (Trained) | Measurement Method |
| :--- | :---: | :---: | :--- |
| **Accuracy against Reference Labels** | N/A *(Baseline Ground Truth)* | **Yes** | $\text{Accuracy} = \frac{\sum \mathbb{I}(y = \hat{y})}{N}$ on 80/20 test split |
| **Precision, Recall, Macro-F1** | N/A | **Yes** | Multiclass macro-averaged metrics across action classes |
| **Player Win Rate** | **Yes** | **Yes** *(Target: Lower)* | $\frac{\text{Player Wins}}{\text{Total Matches}} \times 100\%$ |
| **Average Fight Duration** | **Yes** | **Yes** | Mean survival time per encounter (seconds) |
| **Damage Dealt (Boss DPS & Total)** | **Yes** | **Yes** | Total HP taken and average damage per combat second |
| **Inference / Decision Latency** | **Yes** *(Rule Execution: $\sim 0.45\ \mu s$)* | **Yes** *(ML Prediction: $\sim 1.8\text{ ms}$)* | High-precision stopwatch timing |
| **Action Diversity (Shannon Entropy)** | **Yes** *(Low / predictable)* | **Yes** *(High / varied)* | $H(A) = -\sum_{i=1}^K P(a_i) \log_2 P(a_i)$ |

---

## 4. CSE3705 Course Syllabus Alignment

Detailed specifications for each syllabus topic are provided in [`CSE3705_AI_COURSE_FEATURES_AND_PROJECT_BLUEPRINT.md`](CSE3705_AI_COURSE_FEATURES_AND_PROJECT_BLUEPRINT.md):

* **Topic 1 & 5 (Intelligent Agents & PEAS):** Formal PEAS formulation comparing Reflex vs. Learning Agents.
* **Topic 2 (Search & Problem Solving):** Discrete A* grid pathfinding over arena hazard zones ($f(n) = g(n) + h(n)$).
* **Topic 3 (Planning & Constraint Satisfaction):** Boss 3-hit attack combo planner formulated as a Constraint Satisfaction Problem (CSP).
* **Topic 4 (Knowledge Representation & Logic):** First-Order Predicate Logic (FOL) tactical decision rules and forward chaining inference.
* **Topics 6 & 7 (Generative AI & Data Synthesis):** Conditional Tabular GAN (CTGAN) / VAE for synthetic combat telemetry data augmentation.
* **Topic 8 (Transformer Architectures):** Temporal Self-Attention Transformer for 5-frame player move sequence prediction.
* **Topic 9 (LLMs, LangChain & Responsible AI):** Post-match tactical analysis debrief via local LLM and ethical AI safety analysis.

---

## 5. Repository Structure

```
COMBAT-E-ML/
├── Assets/                                  # Unity source code and game assets
│   └── _Project/
│       └── Scripts/
│           ├── AI/
│           │   ├── BossController.cs        # Primary Boss Agent controller
│           │   ├── RuleBasedBossBrain.cs    # Deterministic Rule-Based Expert System
│           │   ├── BossAttackSelector.cs    # Phase attack selection
│           │   └── ...
│           ├── Combat/                      # Hitboxes, vitals, damage calculations
│           ├── Player/                      # Player state machine and movement
│           └── Managers/
│               ├── TelemetryManager.cs      # 52-column real-time telemetry recorder
│               └── GameBootstrapper.cs      # Automated initialization
├── train_boss_models.py                     # Multiclass ML Training Pipeline
├── evaluate_boss.py                         # Evaluation & comparative benchmark suite
├── export_excel.py                          # Multi-sheet Excel data export
├── gameplay_telemetry.csv                   # Raw telemetry stream from gameplay
├── gameplay_telemetry.xlsx                  # Formatted Excel sheet + Variable Dictionary
└── CSE3705_AI_COURSE_FEATURES_AND_PROJECT_BLUEPRINT.md # Full Academic Report Blueprint
```

---

## 6. How to Run

### Unity Combat Arena
1. Open the project in **Unity 2022.3 LTS** (or compatible 2022.x/2023.x version).
2. Open `Assets/_Project/Scenes/Arena.unity`.
3. Press **Play**.
4. **Controls**:
   * **W, A, S, D**: Movement
   * **Left Mouse Button / J**: Attack Combo
   * **Right Mouse Button / K**: Guard / Precision Deflect
   * **Spacebar**: Dash / Dodge Roll
   * **Q / E**: Healing Flask

### Running ML Training & Evaluation
```bash
# Install Python dependencies
pip install numpy pandas scikit-learn openpyxl joblib

# Evaluate current telemetry data
python evaluate_boss.py

# Train ML classifiers on collected combat data
python train_boss_models.py
```

---

## 7. License & Academic Integrity
This project is submitted in fulfillment of the academic requirements of **CSE3705: Artificial Intelligence**, BML Munjal University.
All core architecture, telemetry implementations, and machine learning pipelines are developed for original research and comparative benchmarking.
