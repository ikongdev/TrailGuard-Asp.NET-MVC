FROM python:3.14.6-slim-bookworm@sha256:ff83a535339812dd72e69c93b3c48ddf7c85a324d6330af5797c82a255dbeef4
ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    TZ=Asia/Manila
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libgomp1 tzdata ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --uid 10001 --create-home app
WORKDIR /app
COPY requirements.txt requirements.linux.lock.txt ./
COPY frozen-bundle/ ./frozen-bundle/
RUN cd frozen-bundle \
    && test "$(wc -l < SHA256SUMS.txt)" -eq 20 \
    && test "$(find . -type f | wc -l)" -eq 21 \
    && sha256sum --check --strict SHA256SUMS.txt
# Resolve all dependencies, including Linux-only ones, without replacing frozen pins.
RUN python -m pip install --no-cache-dir --no-compile -r requirements.linux.lock.txt \
    && python -m pip check \
    && python -m pip freeze --all > /opt/trailguard-installed.txt
COPY app.py adapter.py ./
USER app
EXPOSE 8011
# The planned App Service sidecar shares the MVC container's network namespace.
CMD ["python", "-m", "uvicorn", "app:app", "--host", "127.0.0.1", "--port", "8011", "--workers", "1"]
