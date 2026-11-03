"""
CSE3705 Artificial Intelligence - ML Combat Model Training Pipeline
Trains Supervised Multiclass Classifiers to predict the Boss's next tactical action
using gameplay telemetry collected during fights against the Rule-Based Boss.

Models Evaluated:
1. Logistic Regression (Baseline Linear Classifier)
2. Decision Tree Classifier (Interpretable Hierarchical Rules)
3. Random Forest (Ensemble Bagging)
4. Gradient Boosting / Histogram-based GB (Ensemble Boosting)

Metrics Calculated:
- Accuracy against Reference Labels
- Precision, Recall, Macro-F1
- Inference / Prediction Latency (ms)
- Feature Importance Rankings
"""

import os
import sys
import time
import numpy as np
import pandas as pd
from sklearn.model_selection import train_test_split
from sklearn.preprocessing import StandardScaler
from sklearn.metrics import accuracy_score, precision_score, recall_score, f1_score, classification_report, confusion_matrix
from sklearn.linear_model import LogisticRegression
from sklearn.tree import DecisionTreeClassifier
from sklearn.ensemble import RandomForestClassifier, HistGradientBoostingClassifier
import joblib

TELEMETRY_CSV = "gameplay_telemetry.csv"
MODEL_OUTPUT_PATH = "boss_action_model.pkl"

# Feature set extracted from the 52-column telemetry stream
FEATURE_COLUMNS = [
    "distance_to_boss",
    "relative_angle_deg",
    "player_hp",
    "player_hp_pct",
    "player_stamina",
    "player_stamina_pct",
    "player_posture",
    "player_posture_pct",
    "player_is_guarding",
    "player_is_invulnerable",
    "boss_hp",
    "boss_hp_pct",
    "boss_poise",
    "boss_poise_pct",
    "boss_phase",
    "count_player_attacks",
    "count_player_dodges",
    "count_player_deflects",
    "count_player_blocks",
    "count_player_hits_taken",
    "count_boss_attacks",
    "count_boss_parries",
    "count_boss_hits_taken"
]

TARGET_COLUMN = "boss_action"


def load_telemetry(csv_path: str):
    if not os.path.exists(csv_path):
        raise FileNotFoundError(f"Telemetry dataset not found at '{csv_path}'. Please play a few matches against the Rule-Based Boss to generate data.")

    df = pd.read_csv(csv_path)
    print(f"[Telemetry] Loaded {len(df)} total rows across {df['game_id'].nunique()} games.")

    # Filter out rows where boss is not performing an attack action
    df = df[df[TARGET_COLUMN].notna()]
    df = df[df[TARGET_COLUMN] != "None"]

    # Drop rows where target action appears less than twice (cannot be stratified)
    counts = df[TARGET_COLUMN].value_counts()
    valid_classes = counts[counts >= 2].index
    df = df[df[TARGET_COLUMN].isin(valid_classes)]

    print(f"[Telemetry] Filtered to {len(df)} active action decision samples across {len(valid_classes)} classes:")
    for cls_name, count in counts.items():
        if cls_name in valid_classes:
            print(f"  - {cls_name}: {count} occurrences ({count / len(df) * 100:.1f}%)")

    return df


def train_and_evaluate(df: pd.DataFrame):
    X = df[FEATURE_COLUMNS].copy()
    y = df[TARGET_COLUMN].copy()

    # Encode categorical player states if present
    if "player_state" in df.columns:
        X["player_state_encoded"] = df["player_state"].astype("category").cat.codes

    # Handle missing / NaN values with median imputation
    X = X.fillna(X.median())

    # Stratified Train/Test Split (80% Train, 20% Evaluation)
    X_train, X_test, y_train, y_test = train_test_split(
        X, y, test_size=0.20, random_state=42, stratify=y
    )

    scaler = StandardScaler()
    X_train_scaled = scaler.fit_transform(X_train)
    X_test_scaled = scaler.transform(X_test)

    models = {
        "Logistic Regression": (LogisticRegression(max_iter=1000, multi_class="multinomial", random_state=42), True),
        "Decision Tree": (DecisionTreeClassifier(max_depth=6, random_state=42), False),
        "Random Forest": (RandomForestClassifier(n_estimators=100, max_depth=8, random_state=42), False),
        "Histogram Gradient Boosting": (HistGradientBoostingClassifier(max_iter=100, random_state=42), False)
    }

    results = []
    best_f1 = -1.0
    best_model_name = ""
    best_pipeline = None

    print("\n" + "=" * 80)
    print("CSE3705 EXPERIMENTAL EVALUATION: MODEL TRAINING & BENCHMARKING")
    print("=" * 80)

    for name, (model, requires_scaling) in models.items():
        X_tr = X_train_scaled if requires_scaling else X_train
        X_te = X_test_scaled if requires_scaling else X_test

        # Train model
        model.fit(X_tr, y_train)

        # Benchmark Inference Latency
        latencies = []
        for _ in range(100):
            single_sample = X_te.iloc[0:1] if not requires_scaling else X_te[0:1]
            start_t = time.perf_counter()
            _ = model.predict(single_sample)
            latencies.append((time.perf_counter() - start_t) * 1000.0)  # to ms
        avg_latency_ms = np.mean(latencies)

        # Predictions on test set
        y_pred = model.predict(X_te)

        acc = accuracy_score(y_test, y_pred)
        prec = precision_score(y_test, y_pred, average="macro", zero_division=0)
        rec = recall_score(y_test, y_pred, average="macro", zero_division=0)
        f1 = f1_score(y_test, y_pred, average="macro", zero_division=0)

        results.append({
            "Model": name,
            "Accuracy": acc,
            "Precision (Macro)": prec,
            "Recall (Macro)": rec,
            "F1-Score (Macro)": f1,
            "Prediction Latency (ms)": avg_latency_ms
        })

        if f1 > best_f1:
            best_f1 = f1
            best_model_name = name
            best_pipeline = (model, scaler if requires_scaling else None, list(X.columns))

    results_df = pd.DataFrame(results)
    print("\n" + results_df.to_string(index=False, justify="center"))

    print("\n" + "-" * 80)
    print(f"Top Performing Architecture: {best_model_name} (Macro-F1 = {best_f1:.4f})")
    print("-" * 80)

    # Save best model to disk
    joblib.dump(best_pipeline, MODEL_OUTPUT_PATH)
    print(f"[Model Registry] Serialized top model artifact to '{MODEL_OUTPUT_PATH}'")

    return results_df, best_model_name


if __name__ == "__main__":
    if len(sys.argv) > 1:
        TELEMETRY_CSV = sys.argv[1]

    try:
        data = load_telemetry(TELEMETRY_CSV)
        train_and_evaluate(data)
    except Exception as e:
        print(f"[Error] {e}")

