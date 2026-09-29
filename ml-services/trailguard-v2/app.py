"""TrailGuard ML v2 adapter API; separate from the historical baseline service."""

from __future__ import annotations

import logging
from typing import Any

from fastapi import FastAPI, HTTPException
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from adapter import (
    BINARY_THRESHOLD,
    BUNDLE,
    MODEL_VERSION,
    RequestContractError,
    UI_POLICY_VERSION,
    create_prediction,
    ui_label_policy,
)


logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s %(name)s: %(message)s",
)
LOGGER = logging.getLogger("trailguard_v2_api")

app = FastAPI(title="TrailGuard ML v2 Adapter", version=MODEL_VERSION)


@app.exception_handler(RequestValidationError)
async def request_validation_error_handler(_, __):
    return JSONResponse(status_code=422, content={"detail": "Invalid prediction request."})


@app.get("/")
@app.get("/health")
def health() -> dict[str, Any]:
    return {
        "status": "ok",
        "service": "TrailGuard ML v2 adapter",
        "model_version": MODEL_VERSION,
        "selected_tree_count": BUNDLE.selected_tree_count,
    }


@app.get("/model-info")
def model_info() -> dict[str, Any]:
    return {
        "model_version": MODEL_VERSION,
        "frozen_manifest_name": BUNDLE.manifest_name,
        "frozen_model_sha256": BUNDLE.model_sha256,
        "selected_tree_count": BUNDLE.selected_tree_count,
        "feature_order": list(BUNDLE.feature_order),
        "categorical_mappings": BUNDLE.categorical_mappings,
        "numeric_constraints": BUNDLE.numeric_constraints,
        "binary_threshold": BINARY_THRESHOLD,
        "ui_label_policy_version": UI_POLICY_VERSION,
        "ui_label_policy": ui_label_policy(),
    }


@app.post("/predict")
def predict(payload: dict[str, Any]) -> dict[str, Any]:
    try:
        return create_prediction(payload)
    except RequestContractError as exc:
        LOGGER.warning("Rejected invalid v2 prediction request: %s", exc)
        raise HTTPException(status_code=422, detail="Invalid prediction request.") from exc
    except RuntimeError as exc:
        LOGGER.error("v2 prediction unavailable: %s", exc)
        raise HTTPException(status_code=503, detail="Prediction service is temporarily unavailable.") from exc
