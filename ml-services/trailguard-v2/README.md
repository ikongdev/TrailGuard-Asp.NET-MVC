# TrailGuard ML v2 adapter

This is a separate FastAPI adapter for the copied, frozen TrailGuard v2 bundle.
It does not modify or call the repository's historical `trailguard-ml-v2` service.

The adapter verifies every file listed in `frozen-bundle/SHA256SUMS.txt` before
loading bundle metadata or inference entry points. It sends validated raw values to
the bundle's own preprocessing and native TreeSHAP implementation.

## Run on a separate port

From Windows CMD in this directory:

```cmd
py -3.14 -m venv .venv
.venv\Scripts\activate
python -m pip install --upgrade pip
python -m pip install -r requirements.txt
python -m uvicorn app:app --host 127.0.0.1 --port 8011
```

The MVC assessment path uses this adapter through `TrailGuardV2ApiClient` and
`TrailGuardV2Api:BaseUrl` (locally `http://127.0.0.1:8011`). Its API exposes `GET /health` (with `GET /` as an alias),
`GET /model-info`, and `POST /predict`.

The model artifact stores 1,000 trees; prediction and native TreeSHAP use only the
selected range `[0, 985)`. Integrity and explanation checks do not establish
predictive performance, calibration, or dataset/agency provenance. Original dataset
and training/final-Test run evidence remain unavailable in the reviewed checkout.

`POST /predict` accepts exactly the 11 frozen feature keys. It rejects extra fields,
including medical and BMI fields, before the frozen bundle validates and scores the
input. No participant request bodies are logged.
