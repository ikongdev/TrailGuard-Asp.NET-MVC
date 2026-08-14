# TrailGuard — Project Context

Capstone project: **TrailGuard — A Web-Based Hiking Event Management System with Machine Learning-Based Participant-to-Trail Suitability Assessment**

PUP College of Computer and Information Sciences. This repo started as an App Dev project (rule-based scoring) and is being extended into the capstone version (ML-based prediction with explainability).

---

## Tech Stack

| Layer | Technology |
|---|---|
| Web framework | ASP.NET Core MVC, .NET 10 (C#) |
| ORM | Entity Framework Core 10.0.10 |
| Database | PostgreSQL 18 (via Npgsql provider) — **migrated from MySQL** |
| Auth | ASP.NET Core Identity (roles: Admin, Organizer, Participant) |
| Frontend | Razor views + Tailwind CSS (glassmorphism dark theme) |
| ML service | Python 3.14 + FastAPI + XGBoost + SHAP (separate process) |
| Local dev | PostgreSQL local install, no Docker |
| Planned cloud DB | Aiven (PostgreSQL free tier) — not yet deployed |

### Running the project

Two processes must run simultaneously:

```bash
# Terminal 1 — ML service
cd TrailGuard-ML
python -m uvicorn main:app --reload --port 8000

# Terminal 2 — Web app
dotnet run
```

DB credentials live in **User Secrets**, not `appsettings.json` (which holds a `SET_IN_USER_SECRETS` placeholder).

---

## Architecture

```
ASP.NET Core MVC (C#)  ──HTTP/JSON──▶  Python FastAPI (XGBoost + SHAP)
        │                                    localhost:8000/predict
        ▼
   PostgreSQL
```

The ML model cannot run in-process because XGBoost/SHAP are Python-only. `SuitabilityApiClient.cs` bridges the two via `HttpClient`, configured in `Program.cs` with a 10-second timeout and base URL from `appsettings.json` (`MlApi:BaseUrl`).

**Fallback behavior:** if the ML API is unreachable or times out, `AssessmentController` falls back to the legacy rule-based `GetResult()`. This is intentional and should be preserved — it's a defensible resilience feature.

---

## ML Pipeline (`TrailGuard-ML/`)

| File | Purpose |
|---|---|
| `generate_synthetic_dataset.py` | Generates 2,000-row rule-derived synthetic dataset |
| `train_model.py` | Trains XGBoost with default hyperparameters |
| `tune_model.py` | Grid search tuning; overwrites the saved model |
| `test_shap.py` | Verifies SHAP explainer setup |
| `main.py` | FastAPI service exposing `POST /predict` |
| `trailguard_xgboost_model.json` | Trained model (committed to repo) |
| `label_encoder.pkl` | Class index → label mapping (committed) |

**Current performance:** 80.50% accuracy, F1 (weighted) 0.8072, model version `v1-synthetic`.

### Synthetic dataset basis (cite these in the manuscript)

- **Trail demand** — Shenandoah National Park trail difficulty formula: `sqrt(elevation_gain_ft × 2 × distance_miles)`, with a terrain multiplier (1.00 paved / 1.10 rocky / 1.25 technical)
- **Fitness scoring** — ACSM Physical Activity Guidelines (150 min/week moderate, or 3×20 min vigorous)
- **BMI** — WHO BMI classification
- **Gear** — "Ten Essentials Systems" (The Mountaineers)
- **Health flags** — binary risk indicators; weights pending expert validation

Labels are assigned by z-score standardizing trail demand and participant readiness separately, then thresholding the gap. Small Gaussian noise is added so the task isn't perfectly separable.

**Weather is deliberately excluded from ML features** — it isn't available at registration time (forecasts only exist near the event date), and there's no historical weather-incident data to build synthetic rules from. Weather remains a separate day-of advisory feature.

### Feature mapping (C# → Python)

`AssessmentController` has `MapExerciseFrequency`, `MapMountainsClimbed`, `MapTerrainType`, etc. — these convert raw form string answers into the 0–3 ordinal scores the model expects. The legacy `ComputeFitnessScore`/`ComputeExperienceScore`/etc. (3–12 scale) are **still used** for the old category-score display, so don't delete them.

### Label format gotcha

The ML API returns `"Good Match"` (space). The existing codebase uses `"Good-Match"` (hyphen) throughout `ComputeRecommendations`, `GetAlternativeEvents`, and views. `NormalizeLabel()` in `AssessmentController` converts ML output to the hyphenated form. **Keep this** — changing every usage site is riskier.

---

## Key Domain Rules

**Suitability labels:** `Good-Match`, `Borderline`, `Not Recommended`

**Registration statuses:** `Pending` → `Awaiting Payment` → `For Payment Verification` → `Accepted`, plus `Rejected`, `Cancelled`, `Voided`

**Explainability is required, not optional.** SHAP breakdowns appear on both the participant assessment report ("Why This Result?", orange accent, "Helped"/"Reduced" wording) and the organizer registration review ("Assessment Explanation", blue accent, "Supported"/"Weakened" wording, plus a disclaimer that ML output is decision support only). `ShapHelper.cs` holds the shared display logic and friendly feature-name mapping.

**The system never auto-approves or auto-rejects.** The ML result is decision support; the organizer always makes the final call. This is stated explicitly in the manuscript's Limitations section.

---

## Conventions

- Tailwind classes only — no custom CSS files. Cards use `bg-slate-900/60 backdrop-blur-xl border border-white/10 rounded-2xl`
- Organizer views use blue accents (`text-blue-400`); participant views use orange (`text-orange-400`)
- Progress bars: `w-full h-2 bg-gray-700 rounded-full overflow-hidden` with an inner div sized by inline `style="width: X%"`
- Controllers pass data via strongly-typed ViewModels where one exists, `ViewBag` otherwise
- `DateTime.Now` (not UtcNow) is used throughout; `Program.cs` sets `Npgsql.EnableLegacyTimestampBehavior` to allow this with PostgreSQL

---

## Current State

**Working end-to-end:**
- Auth with three roles, trail CRUD, event CRUD, event browsing
- Assessment form → ML prediction → result stored in `SuitabilityResults` + `ShapValues`
- SHAP explanation panels on both participant and organizer sides
- Registration submission, organizer approve/reject, alternative event recommendation
- Post-event feedback (participant) and post-event assessment (organizer)
- Records page with CSV export
- Weather forecast via Open-Meteo API (in `EventController`)

**Known gaps vs. the manuscript** (see `PLAN.md` for the active work):
1. Result-based registration workflow — medical clearance, preparation plan, payment flow rework ← **in progress**
2. Event completion confirmation + persisted final suitability labels
3. Organizer decision reason isn't saved
4. `Event` lacks `Notes` and `Reminders` fields
5. Weather risk level + suggested reminder (rule-based) not implemented

---

## Working Style

- Commit per phase, not all at once — easier to review and revert
- Run `dotnet build` after each set of edits before moving on
- Migrations: `dotnet ef migrations add <Name>` then `dotnet ef database update`
- When a migration warns about possible data loss, read the generated migration file before applying it
