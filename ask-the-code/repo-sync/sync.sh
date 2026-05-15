#!/bin/sh
set -eu

: "${GIT_REPO_URL:?GIT_REPO_URL is required}"
: "${GITHUB_TOKEN:?GITHUB_TOKEN is required}"
GIT_BRANCH="${GIT_BRANCH:-main}"
SYNC_INTERVAL_SECONDS="${SYNC_INTERVAL_SECONDS:-300}"
REPO_DIR="/repo"

log() {
    printf '{"ts":"%s","service":"repo-sync","level":"%s","msg":"%s"}\n' \
        "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$1" "$2"
}

# Inject the token via URL rewrite (in memory) so we never persist it on disk.
AUTH_URL="$(printf '%s' "$GIT_REPO_URL" | sed -E "s#https://#https://x-access-token:${GITHUB_TOKEN}@#")"

git config --global advice.detachedHead false
git config --global safe.directory "$REPO_DIR"

if [ ! -d "$REPO_DIR/.git" ]; then
    log info "cloning repository"
    rm -rf "${REPO_DIR:?}/"* "${REPO_DIR:?}/".[!.]* 2>/dev/null || true
    git clone --depth 1 --branch "$GIT_BRANCH" "$AUTH_URL" "$REPO_DIR"
    log info "initial clone complete"
else
    log info "repository already present, will pull"
fi

# Strip credentials from origin so subsequent inspections (logs/ps) don't leak.
cd "$REPO_DIR"
git remote set-url origin "$GIT_REPO_URL"

while true; do
    cd "$REPO_DIR"
    # Re-inject auth for the fetch only.
    if git -c "http.extraHeader=Authorization: Bearer ${GITHUB_TOKEN}" fetch origin "$GIT_BRANCH" --depth 1 2>/dev/null; then
        if git reset --hard "origin/${GIT_BRANCH}" >/dev/null 2>&1; then
            HEAD_SHA="$(git rev-parse --short HEAD)"
            log info "pulled ${GIT_BRANCH}@${HEAD_SHA}"
        else
            log error "failed to reset to origin/${GIT_BRANCH}"
        fi
    else
        log error "git fetch failed"
    fi
    sleep "$SYNC_INTERVAL_SECONDS"
done
