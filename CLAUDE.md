# TrailGuard — Current implementation guide

This concise guide is aligned with AGENTS.md, the complete repository operating guide. Follow its authorization safeguards, verification and handoff requirements, Event-retention rules, UI guidance, and model safety boundaries.

## Active system

- Active model: trailguard-v2.0.0, frozen at 11 supported features and 985 selected XGBoost trees.
- Active service: ml-services/trailguard-v2 FastAPI adapter, port 8011 locally.
- MVC integration: TrailGuardV2ApiClient, TrailGuardV2AssessmentRequestMapper, and TrailGuardV2Api:BaseUrl.
- Historical trailguard-ml-v2, AssessmentController.BuildMlRequest, and historical encoding.py do not govern active inference.
- Azure deployment runs the MVC container on 8080 with the Python sidecar on 8011, using Supabase PostgreSQL.

Never restore a rule-based fallback. Invalid/unavailable inference produces no substitute result and persists no partial assessment. Organizer review remains the final registration decision.

## Model-facing rules

The 11 inputs are exercise frequency, cardio duration, exercise consistency, hiking experience, hiking recency, hardest trail completed, gear score, distance, elevation gain, trail class, and typical duration. Categorical strings, numeric constraints, input order, model SHA-256, and tree count are frozen and must fail closed on mismatch.

BMI, medical conditions, demographics, and weather are not active-model inputs. Medical screening remains separate C# registration logic. Trail values come from immutable Event snapshots, including TrailDurationHoursSnapshot, never EstimatedDuration or a live Trail.

ModelScore is unchanged probability_yes, rendered to two decimal percentage places with <0.01%/>99.99% behavior. Labels are interpretation bands over that single binary score: Not Recommended below 0.30, Borderline from 0.30 to below 0.80, and Good Match at or above 0.80. Do not call it a calibrated real-world completion probability.

Native TreeSHAP is returned on the raw-margin/log-odds scale. Its integrity/additivity/reconstruction checks are explanation consistency checks, not predictive-accuracy evidence. Factor display orders all inputs by absolute impact. Suggestions use up to three negative actionable factors only: exercise frequency, cardio duration, exercise consistency, and gear score.

## Current application notes

Difficulty is a separate provisional route-effort display policy with Minor Hike, Major Hike, and Major Hike - Difficult labels. Event snapshots preserve assessed inputs. Events are retained; cancellation closes future workflow processing without rewriting history. Add Trail has pending-state duplicate protection; Question 9 has technical class descriptions; the public Privacy page is responsive; Popular Trails has corrected stats and mobile swipe navigation with hidden scrollbar.

Evaluation/calibration, agency provenance for the frozen model, and Privacy contact/retention details remain unresolved. The wording “an empirical dataset provided by our partner hiking agency” must not be used until source documentation and a training/run record link that dataset to this exact frozen model.

Preserve the role boundary (Admin, Organizer, Participant), antiforgery, ownership checks, workflow locks, and private-upload authorization. Cancellation retains Event history but closes new registration, assessment, payment, approval, verification, and alternative-recommendation processing without rewriting retained records.
