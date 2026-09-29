"""HTTP-facing safeguards around the frozen TrailGuard v2 inference bundle.

The frozen bundle owns feature encoding, model inference, and native TreeSHAP.
This module verifies the copied artifact before use, rejects malformed raw input
without coercion, and applies the separately approved UI-label policy.
"""

from __future__ import annotations

import hashlib
import json
import logging
import math
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any


LOGGER = logging.getLogger("trailguard_v2_adapter")

# The copied bundle is an immutable artifact. Do not create bytecode beside it.
sys.dont_write_bytecode = True

SERVICE_DIRECTORY = Path(__file__).resolve().parent
BUNDLE_DIRECTORY = SERVICE_DIRECTORY / "frozen-bundle"
MODEL_VERSION = "trailguard-v2.0.0"
UI_POLICY_VERSION = "inherited-0.30-0.80"
BINARY_THRESHOLD = 0.5
BORDERLINE_THRESHOLD = 0.30
GOOD_MATCH_THRESHOLD = 0.80
EXPECTED_TREE_COUNT = 985
EXPECTED_FROZEN_MODEL_SHA256 = "bdc93bff426fadbcab6d4ce16b4a457050a6875fb67c355df0da5101caeb4e35"
REQUIRED_RUNTIME_ARTIFACTS = frozenset(
    {
        "selected_model.ubj",
        "manifest.json",
        "feature_schema.json",
        "training_configuration.json",
        "trailguard_v2_inference/__init__.py",
        "trailguard_v2_inference/constants.py",
        "trailguard_v2_inference/numeric_validation.py",
        "trailguard_v2_inference/preprocessing.py",
        "trailguard_v2_inference/predict.py",
        "trailguard_v2_inference/shap_support.py",
        "trailguard_v2_inference/explain.py",
    }
)
SHA256SUM_ENTRY = re.compile(r"(?P<digest>[0-9a-f]{64})  (?P<name>[^\r\n]+)$")
EXPECTED_CATEGORICAL_FEATURES = frozenset(
    {
        "exercise_frequency",
        "cardio_duration",
        "exercise_consistency",
        "hiking_experience",
        "hiking_recency",
        "hardest_trail_completed",
    }
)
EXPECTED_NUMERIC_FEATURES = frozenset(
    {
        "gear_score",
        "distance_km",
        "elevation_gain_m",
        "trail_class",
        "typical_duration_hours",
    }
)


class BundleIntegrityError(RuntimeError):
    """The copied frozen bundle is absent or does not match its manifest."""


class RequestContractError(ValueError):
    """A request does not meet the frozen bundle's raw input contract."""


@dataclass(frozen=True)
class BundleMetadata:
    feature_order: tuple[str, ...]
    categorical_mappings: dict[str, dict[str, int]]
    numeric_constraints: dict[str, dict[str, float | bool]]
    model_sha256: str
    selected_tree_count: int
    manifest_name: str


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def verify_bundle_integrity(bundle_directory: Path = BUNDLE_DIRECTORY) -> None:
    """Verify a complete, non-escaping frozen runtime bundle before inference."""
    bundle_root = Path(bundle_directory).resolve()
    sums_path = bundle_root / "SHA256SUMS.txt"
    if not sums_path.is_file():
        raise BundleIntegrityError("Frozen bundle integrity manifest is missing.")

    try:
        lines = sums_path.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeDecodeError) as exc:
        raise BundleIntegrityError("Frozen bundle integrity manifest cannot be read.") from exc
    if not lines or not any(line.strip() for line in lines):
        raise BundleIntegrityError("Frozen bundle integrity manifest is empty.")

    failures: list[str] = []
    covered_artifacts: set[str] = set()
    observed_digests: dict[str, str] = {}
    for line_number, line in enumerate(lines, start=1):
        match = SHA256SUM_ENTRY.fullmatch(line)
        if match is None:
            failures.append(f"malformed SHA256SUMS entry at line {line_number}")
            continue
        try:
            artifact = (bundle_root / match["name"]).resolve()
            relative_name = artifact.relative_to(bundle_root).as_posix()
        except (OSError, RuntimeError, ValueError):
            failures.append(f"SHA256SUMS path escapes the bundle at line {line_number}")
            continue
        artifact_key = relative_name.casefold()
        if artifact_key in covered_artifacts:
            failures.append(f"duplicate SHA256SUMS entry for {relative_name}")
            continue
        covered_artifacts.add(artifact_key)
        if not artifact.is_file():
            failures.append(f"missing {relative_name}")
            continue
        actual_digest = _sha256(artifact)
        observed_digests[artifact_key] = actual_digest
        if actual_digest != match["digest"]:
            failures.append(f"checksum mismatch for {relative_name}")

    missing_runtime_artifacts = REQUIRED_RUNTIME_ARTIFACTS - covered_artifacts
    if missing_runtime_artifacts:
        failures.append("manifest lacks required runtime artifact coverage")
    model_digest = observed_digests.get("selected_model.ubj")
    if model_digest is not None and model_digest != EXPECTED_FROZEN_MODEL_SHA256:
        failures.append("frozen model checksum does not match the pinned adapter checksum")
    if failures:
        raise BundleIntegrityError("; ".join(failures))


def load_bundle_metadata(bundle_directory: Path = BUNDLE_DIRECTORY) -> BundleMetadata:
    """Verify and read only the immutable bundle metadata needed by the adapter."""
    verify_bundle_integrity(bundle_directory)
    schema = json.loads((bundle_directory / "feature_schema.json").read_text(encoding="utf-8"))
    manifest = json.loads((bundle_directory / "manifest.json").read_text(encoding="utf-8"))
    configuration = json.loads((bundle_directory / "training_configuration.json").read_text(encoding="utf-8"))

    feature_order = tuple(schema["feature_order"])
    if len(feature_order) != 11:
        raise BundleIntegrityError("Frozen bundle does not expose exactly 11 model features.")
    categorical_mappings = schema["categorical_mappings"]
    numeric_constraints = _load_numeric_constraints(bundle_directory)
    if set(categorical_mappings) != EXPECTED_CATEGORICAL_FEATURES:
        raise BundleIntegrityError("Frozen bundle categorical feature contract is invalid.")
    if set(numeric_constraints) != EXPECTED_NUMERIC_FEATURES:
        raise BundleIntegrityError("Frozen bundle numeric feature contract is invalid.")
    if set(feature_order) != EXPECTED_CATEGORICAL_FEATURES | EXPECTED_NUMERIC_FEATURES:
        raise BundleIntegrityError("Frozen bundle feature order is invalid.")
    if configuration.get("selected_tree_count") != EXPECTED_TREE_COUNT:
        raise BundleIntegrityError("Frozen bundle selected tree count is not 985.")
    if configuration.get("threshold") != BINARY_THRESHOLD:
        raise BundleIntegrityError("Frozen bundle binary threshold is not 0.5.")

    model_file = manifest.get("model_file")
    if not isinstance(model_file, str) or not model_file:
        raise BundleIntegrityError("Frozen bundle model file is not declared.")
    model_path = bundle_directory / model_file
    if not model_path.is_file():
        raise BundleIntegrityError("Frozen bundle model file is missing.")
    if model_file != "selected_model.ubj" or _sha256(model_path) != EXPECTED_FROZEN_MODEL_SHA256:
        raise BundleIntegrityError("Frozen bundle model does not match the pinned adapter model.")

    return BundleMetadata(
        feature_order=feature_order,
        categorical_mappings=categorical_mappings,
        numeric_constraints=numeric_constraints,
        model_sha256=EXPECTED_FROZEN_MODEL_SHA256,
        selected_tree_count=configuration["selected_tree_count"],
        manifest_name=manifest.get("manifest_name", "TrailGuard v2"),
    )


def _load_numeric_constraints(bundle_directory: Path) -> dict[str, dict[str, float | bool]]:
    """Read the immutable numeric constraint constants without recreating inference."""
    bundle_path = str(bundle_directory)
    if bundle_path not in sys.path:
        sys.path.insert(0, bundle_path)
    from trailguard_v2_inference.constants import NUMERIC_CONSTRAINTS

    return {name: dict(constraint) for name, constraint in NUMERIC_CONSTRAINTS.items()}


BUNDLE = load_bundle_metadata()

# Import only after integrity validation. The bundle owns preprocessing and TreeSHAP.
from trailguard_v2_inference.explain import explain_row  # noqa: E402


def _is_real_number(value: Any) -> bool:
    if isinstance(value, bool):
        return False
    if isinstance(value, int):
        # Do not coerce arbitrary-size integers to float: that can raise OverflowError.
        return -sys.float_info.max <= value <= sys.float_info.max
    return isinstance(value, float) and math.isfinite(value)


def validate_raw_payload(payload: Any) -> dict[str, Any]:
    """Reject invalid raw JSON without normalizing it before frozen validation."""
    if not isinstance(payload, dict):
        raise RequestContractError("request body must be a JSON object")

    actual = set(payload)
    expected = set(BUNDLE.feature_order)
    if actual != expected:
        missing = expected - actual
        extra = actual - expected
        if missing:
            raise RequestContractError("request is missing required model features")
        if extra:
            raise RequestContractError("request contains unsupported model features")
        raise RequestContractError("request feature set is invalid")

    for feature, mapping in BUNDLE.categorical_mappings.items():
        value = payload[feature]
        if not isinstance(value, str) or value not in mapping:
            raise RequestContractError(f"invalid category for {feature}")

    for feature, constraint in BUNDLE.numeric_constraints.items():
        value = payload[feature]
        if not _is_real_number(value):
            raise RequestContractError(f"invalid numeric value for {feature}")
        if constraint["integer"] and value % 1 != 0:
            raise RequestContractError(f"{feature} must be a whole number")
        if "minimum" in constraint and value < constraint["minimum"]:
            raise RequestContractError(f"{feature} is below the allowed minimum")
        if "exclusive_minimum" in constraint and value <= constraint["exclusive_minimum"]:
            raise RequestContractError(f"{feature} is below the allowed minimum")
        if value > constraint["maximum"]:
            raise RequestContractError(f"{feature} exceeds the allowed maximum")

    # This canonical object controls only request key order. Values are unchanged.
    return {feature: payload[feature] for feature in BUNDLE.feature_order}


def ui_label_for_score(score: float) -> str:
    """Apply the inherited display policy without changing the binary model decision."""
    if not _is_real_number(score) or score < 0 or score > 1:
        raise ValueError("model score must be a finite value in [0, 1]")
    if score >= GOOD_MATCH_THRESHOLD:
        return "Good Match"
    if score >= BORDERLINE_THRESHOLD:
        return "Borderline"
    return "Not Recommended"


def ui_label_policy() -> dict[str, Any]:
    """Describe the approved display policy without presenting it as model tuning."""
    return {
        "good_match": {"operator": ">=", "threshold": GOOD_MATCH_THRESHOLD},
        "borderline": {
            "minimum_operator": ">=",
            "minimum": BORDERLINE_THRESHOLD,
            "maximum_operator": "<",
            "maximum": GOOD_MATCH_THRESHOLD,
        },
        "not_recommended": {"operator": "<", "threshold": BORDERLINE_THRESHOLD},
    }


def _binary_prediction(score: float) -> str:
    return "Yes" if score >= BINARY_THRESHOLD else "No"


def create_prediction(payload: Any) -> dict[str, Any]:
    """Run the frozen one-row explanation entry point and shape its verified output."""
    canonical_payload = validate_raw_payload(payload)
    try:
        result = explain_row(BUNDLE_DIRECTORY, canonical_payload)
    except Exception as exc:  # Frozen preprocessing or TreeSHAP failed closed.
        LOGGER.error("Frozen v2 inference failed (%s).", type(exc).__name__)
        raise RuntimeError("Frozen inference failed.") from exc

    score = result.get("probability_yes")
    binary_prediction = result.get("predicted_completion")
    verification = result.get("verification")
    features = result.get("features")
    if (
        not _is_real_number(score)
        or score < 0
        or score > 1
        or result.get("threshold") != BINARY_THRESHOLD
        or result.get("selected_tree_count") != EXPECTED_TREE_COUNT
        or binary_prediction != int(score >= BINARY_THRESHOLD)
        or not isinstance(verification, dict)
        or verification.get("within_tolerance") is not True
        or verification.get("predictions_unchanged") is not True
        or not isinstance(features, list)
        or [item.get("feature") for item in features] != list(BUNDLE.feature_order)
        or len(features) != len(BUNDLE.feature_order)
    ):
        LOGGER.error("Frozen v2 inference returned an invalid or unverified result.")
        raise RuntimeError("Frozen inference returned an invalid result.")

    return {
        "model_version": MODEL_VERSION,
        "frozen_model_sha256": BUNDLE.model_sha256,
        "selected_tree_count": EXPECTED_TREE_COUNT,
        "model_score": score,
        "score_name": "probability_yes",
        "binary_prediction": _binary_prediction(score),
        "binary_threshold": BINARY_THRESHOLD,
        "ui_label": ui_label_for_score(score),
        "ui_label_policy_version": UI_POLICY_VERSION,
        "ui_label_policy": ui_label_policy(),
        "shap": {
            "base_value": result["base_value"],
            "raw_margin": result["model_margin"],
            "scale": result["explanation_scale"],
            "contribution_interpretation": result["contribution_interpretation"],
            "contributions": features,
            "verification": verification,
        },
    }
