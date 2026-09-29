from __future__ import annotations

import json
import os
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from unittest.mock import patch
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

SERVICE_DIRECTORY = Path(__file__).resolve().parents[1]
if str(SERVICE_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(SERVICE_DIRECTORY))

import adapter
import app as api


EXAMPLE_PATH = SERVICE_DIRECTORY / "frozen-bundle" / "example_input.json"
EXPLANATION_PATH = SERVICE_DIRECTORY / "frozen-bundle" / "example_explanation_refreshed.json"
MODEL_PATH = SERVICE_DIRECTORY / "frozen-bundle" / "selected_model.ubj"


class AdapterApiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.example = json.loads(EXAMPLE_PATH.read_text(encoding="utf-8"))
        cls.expected = json.loads(EXPLANATION_PATH.read_text(encoding="utf-8"))
        cls.port = cls._available_local_port()
        environment = dict(os.environ, PYTHONDONTWRITEBYTECODE="1")
        cls.server = subprocess.Popen(
            [
                sys.executable,
                "-m",
                "uvicorn",
                "app:app",
                "--host",
                "127.0.0.1",
                "--port",
                str(cls.port),
            ],
            cwd=SERVICE_DIRECTORY,
            env=environment,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        for _ in range(100):
            try:
                if cls._request("/")[0] == 200:
                    break
            except URLError:
                time.sleep(0.1)
        else:
            cls.tearDownClass()
            raise RuntimeError("Timed out waiting for the local adapter API.")

    @classmethod
    def tearDownClass(cls) -> None:
        if hasattr(cls, "server") and cls.server.poll() is None:
            cls.server.terminate()
            cls.server.wait(timeout=10)

    @classmethod
    def _available_local_port(cls) -> int:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
            listener.bind(("127.0.0.1", 0))
            return int(listener.getsockname()[1])

    @classmethod
    def _request(cls, path: str, payload: dict | None = None) -> tuple[int, dict]:
        data = None if payload is None else json.dumps(payload).encode("utf-8")
        request = Request(
            f"http://127.0.0.1:{cls.port}{path}",
            data=data,
            method="GET" if data is None else "POST",
            headers={"Content-Type": "application/json"} if data is not None else {},
        )
        try:
            with urlopen(request, timeout=30) as response:
                return response.status, json.loads(response.read().decode("utf-8"))
        except HTTPError as error:
            try:
                return error.code, json.loads(error.read().decode("utf-8"))
            finally:
                error.close()

    @staticmethod
    def _write_valid_integrity_fixture(root: Path) -> None:
        """Create an isolated checksum-valid runtime fixture, never changing the bundle."""
        manifest_entries: list[str] = []
        for relative_name in sorted(adapter.REQUIRED_RUNTIME_ARTIFACTS):
            artifact = root / relative_name
            artifact.parent.mkdir(parents=True, exist_ok=True)
            if relative_name == "selected_model.ubj":
                shutil.copyfile(MODEL_PATH, artifact)
            else:
                artifact.write_bytes(f"fixture:{relative_name}".encode("utf-8"))
            manifest_entries.append(f"{adapter._sha256(artifact)}  {relative_name}")
        (root / "SHA256SUMS.txt").write_text("\n".join(manifest_entries) + "\n", encoding="utf-8")

    def test_health_and_model_info(self) -> None:
        health_status, health = self._request("/health")
        self.assertEqual(health_status, 200)
        self.assertEqual(health["selected_tree_count"], 985)

        info_status, info = self._request("/model-info")
        self.assertEqual(info_status, 200)
        self.assertEqual(info["feature_order"], list(adapter.BUNDLE.feature_order))
        self.assertEqual(info["frozen_model_sha256"], adapter.BUNDLE.model_sha256)

    def test_recorded_example_uses_frozen_prediction_and_shap(self) -> None:
        status, body = self._request("/predict", self.example)
        self.assertEqual(status, 200)
        self.assertEqual(body["model_version"], "trailguard-v2.0.0")
        self.assertEqual(body["selected_tree_count"], 985)
        self.assertEqual(body["binary_threshold"], 0.5)
        self.assertEqual(
            body["binary_prediction"],
            "Yes" if self.expected["predicted_completion"] == 1 else "No",
        )
        self.assertEqual(body["model_score"], self.expected["probability_yes"])
        self.assertEqual(body["shap"]["verification"], self.expected["verification"])
        self.assertEqual(body["shap"]["contributions"], self.expected["features"])
        self.assertEqual(body["frozen_model_sha256"], adapter.EXPECTED_FROZEN_MODEL_SHA256)

    def test_json_key_order_does_not_change_prediction(self) -> None:
        ordered = {key: self.example[key] for key in reversed(list(self.example))}
        status, body = self._request("/predict", ordered)
        self.assertEqual(status, 200)
        self.assertEqual(body["model_score"], self.expected["probability_yes"])

    def test_binary_and_ui_policy_boundaries_are_separate_from_inference(self) -> None:
        cases = [
            (0.299999, "No", "Not Recommended"),
            (0.30, "No", "Borderline"),
            (0.300001, "No", "Borderline"),
            (0.499999, "No", "Borderline"),
            (0.50, "Yes", "Borderline"),
            (0.500001, "Yes", "Borderline"),
            (0.799999, "Yes", "Borderline"),
            (0.80, "Yes", "Good Match"),
            (0.800001, "Yes", "Good Match"),
        ]
        for score, expected_binary, expected_label in cases:
            with self.subTest(score=score):
                self.assertEqual(adapter._binary_prediction(score), expected_binary)
                self.assertEqual(adapter.ui_label_for_score(score), expected_label)

    def test_invalid_and_extra_raw_inputs_are_rejected(self) -> None:
        invalid_category = dict(self.example, exercise_frequency="3-4")
        extra_medical = dict(self.example, has_cvd=1)
        missing = dict(self.example)
        missing.pop("gear_score")
        invalid_numeric_cases = [
            dict(self.example, distance_km=True),
            dict(self.example, distance_km="8.0"),
            dict(self.example, distance_km=float("nan")),
            dict(self.example, distance_km=float("inf")),
            dict(self.example, distance_km=float("-inf")),
            dict(self.example, gear_score=10**400),
            dict(self.example, gear_score=3.5),
            dict(self.example, distance_km=0),
            dict(self.example, trail_class=5),
        ]

        for payload in (invalid_category, extra_medical, missing, *invalid_numeric_cases):
            with self.subTest(payload=payload):
                status, body = self._request("/predict", payload)
                self.assertEqual(status, 422)
                self.assertEqual(body["detail"], "Invalid prediction request.")

    def test_integrity_validation_rejects_isolated_malformed_fixtures(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "SHA256SUMS.txt").write_text("", encoding="utf-8")
            with self.assertRaisesRegex(adapter.BundleIntegrityError, "empty"):
                adapter.verify_bundle_integrity(root)

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "SHA256SUMS.txt").write_text("not a SHA-256 manifest entry\n", encoding="utf-8")
            with self.assertRaisesRegex(adapter.BundleIntegrityError, "malformed"):
                adapter.verify_bundle_integrity(root)

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            artifact = root / "manifest.json"
            artifact.write_text("fixture", encoding="utf-8")
            (root / "SHA256SUMS.txt").write_text(
                f"{adapter._sha256(artifact)}  manifest.json\n", encoding="utf-8"
            )
            with self.assertRaisesRegex(adapter.BundleIntegrityError, "required runtime artifact coverage"):
                adapter.verify_bundle_integrity(root)

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_valid_integrity_fixture(root)
            changed = root / "trailguard_v2_inference" / "constants.py"
            changed.write_text("changed", encoding="utf-8")
            with self.assertRaisesRegex(adapter.BundleIntegrityError, "checksum mismatch"):
                adapter.verify_bundle_integrity(root)

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_valid_integrity_fixture(root)
            manifest_path = root / "SHA256SUMS.txt"
            first_entry = manifest_path.read_text(encoding="utf-8").splitlines()[0]
            digest, relative_name = first_entry.split("  ", 1)
            manifest_path.write_text(
                manifest_path.read_text(encoding="utf-8") + f"{digest}  {relative_name.upper()}\n",
                encoding="utf-8",
            )
            with self.assertRaisesRegex(adapter.BundleIntegrityError, "duplicate"):
                adapter.verify_bundle_integrity(root)

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "SHA256SUMS.txt").write_text(
                "0" * 64 + "  ../outside-the-bundle.txt\n", encoding="utf-8"
            )
            with self.assertRaisesRegex(adapter.BundleIntegrityError, "escapes"):
                adapter.verify_bundle_integrity(root)

    def test_failed_shap_or_inference_verification_fails_closed(self) -> None:
        unverified = json.loads(json.dumps(self.expected))
        unverified["verification"]["within_tolerance"] = False
        with patch("adapter.explain_row", return_value=unverified):
            with self.assertRaisesRegex(RuntimeError, "invalid result"):
                adapter.create_prediction(self.example)

        with patch("app.create_prediction", side_effect=RuntimeError("frozen check failed")):
            with self.assertRaises(api.HTTPException) as raised:
                api.predict(self.example)
        self.assertEqual(raised.exception.status_code, 503)
        self.assertEqual(raised.exception.detail, "Prediction service is temporarily unavailable.")


if __name__ == "__main__":
    unittest.main()
