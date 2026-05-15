FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    HF_HOME=/opt/hf-cache \
    XDG_CACHE_HOME=/tmp/xdg-cache \
    TRANSFORMERS_OFFLINE=0

RUN apt-get update \
    && apt-get install -y --no-install-recommends build-essential curl ca-certificates tini \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --uid 1000 --create-home --shell /bin/bash app \
    && mkdir -p /opt/hf-cache \
    && chown -R app:app /opt/hf-cache /home/app

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Pre-download every HF model used by our scanners into the image layer.
COPY --chown=app:app preload_models.py ./
USER app
RUN python preload_models.py

USER root
COPY --chown=app:app app.py guards.py rewrite.py mcp_client.py ./
COPY --chown=app:app pipelines/ ./pipelines/

RUN apt-get purge -y --auto-remove build-essential \
    && rm -rf /var/lib/apt/lists/*

USER app

EXPOSE 8000

HEALTHCHECK --interval=15s --timeout=5s --start-period=120s --retries=10 \
    CMD python -c "import urllib.request,sys;\
sys.exit(0 if urllib.request.urlopen('http://127.0.0.1:8000/health',timeout=3).status==200 else 1)"

ENTRYPOINT ["/usr/bin/tini", "--"]
CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8000"]
