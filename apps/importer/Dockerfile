# Build from the repository root so the shared SQL migrations are included:
#   docker build -f apps/importer/Dockerfile .
FROM python:3.14-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    OPENQUEST_MIGRATIONS_DIR=/app/db/migrations \
    OPENQUEST_SNAPSHOT_DIR=/app/data/snapshots

WORKDIR /app/apps/importer

# Dependencies first so code changes don't reinstall them.
COPY apps/importer/pyproject.toml apps/importer/README.md ./
RUN mkdir -p src/openquest_importer && touch src/openquest_importer/__init__.py \
    && pip install . && pip uninstall -y openquest-importer

COPY db/migrations /app/db/migrations
COPY apps/importer/ ./
RUN pip install --no-deps . \
    && useradd --system --uid 10001 --home-dir /app openquest \
    && mkdir -p /app/data/snapshots && chown openquest /app/data/snapshots \
    && chmod +x docker-entrypoint.sh

USER openquest
ENTRYPOINT ["./docker-entrypoint.sh"]
