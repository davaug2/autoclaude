FROM alpine:3.20

RUN apk add --no-cache git ca-certificates tini \
    && adduser -D -u 1000 app \
    && mkdir -p /repo \
    && chown app:app /repo

COPY --chown=app:app sync.sh /usr/local/bin/sync.sh
RUN chmod +x /usr/local/bin/sync.sh

USER app
WORKDIR /repo

ENTRYPOINT ["/sbin/tini", "--"]
CMD ["/usr/local/bin/sync.sh"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD test -d /repo/.git || exit 1
