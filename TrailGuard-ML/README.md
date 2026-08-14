# TrailGuard ML Service

XGBoost-based participant-to-trail suitability classifier with SHAP explainability.

## Setup

Install dependencies:
pip install pandas numpy scikit-learn xgboost shap fastapi uvicorn joblib

## Files

| File | Purpose |
|---|---|
| `generate_synthetic_dataset.py` | Generates the rule-derived synthetic training dataset (2,000 rows) |
| `train_model.py` | Trains the XGBoost classifier with default hyperparameters |
| `tune_model.py` | Runs grid search to find optimal hyperparameters, saves the tuned model |
| `test_shap.py` | Verifies SHAP explainer setup on sample rows |
| `main.py` | FastAPI service exposing the `/predict` endpoint |
| `trailguard_synthetic_dataset.csv` | Generated training data |
| `trailguard_xgboost_model.json` | Trained model (used by the API) |
| `label_encoder.pkl` | Maps class indices to readable labels |

## Running the API
python -m uvicorn main:app --reload --port 8000

Interactive docs available at `http://127.0.0.1:8000/docs`

## Retraining

To regenerate data and retrain from scratch:
python generate_synthetic_dataset.py
python tune_model.py

## Current Model Performance

- Accuracy: 80.50%
- F1 Score (weighted): 0.8072
- Model version: `v1-synthetic`