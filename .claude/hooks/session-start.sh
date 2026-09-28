#!/bin/bash
# SessionStart hook for Claude Code on the web.
# Makes a fresh cloud container able to build and test this repo without human help:
#   - .NET 8 SDK (Ubuntu apt package; builds.dotnet.microsoft.com is not reachable from the sandbox)
#   - npm dependencies for every Vite client under */ClientApp
#   - NuGet restore for the solution
# Idempotent and non-interactive. Runs only in remote (web) sessions.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

ROOT="${CLAUDE_PROJECT_DIR:-$(pwd)}"
export DEBIAN_FRONTEND=noninteractive
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1

log() { echo "[session-start] $*"; }

# 1. .NET SDK 8 --------------------------------------------------------------
if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then
  log "Installing dotnet-sdk-8.0 via apt"
  if command -v sudo >/dev/null 2>&1 && [ "$(id -u)" -ne 0 ]; then SUDO="sudo"; else SUDO=""; fi
  $SUDO apt-get update -qq
  $SUDO apt-get install -y -qq dotnet-sdk-8.0
else
  log ".NET SDK 8 already present: $(dotnet --list-sdks | grep '^8\.' | head -1)"
fi

# Persist the opt-outs for the rest of the session.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
    echo 'export PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1'
  } >> "$CLAUDE_ENV_FILE"
fi

# 1b. dotnet-ef (EF Core migrations CLI) ---------------------------------------
export PATH="$PATH:$HOME/.dotnet/tools"
if ! command -v dotnet-ef >/dev/null 2>&1; then
  log "Installing dotnet-ef 8.0.11"
  dotnet tool install --global dotnet-ef --version 8.0.11 >/dev/null
else
  log "dotnet-ef already present: $(dotnet-ef --version 2>/dev/null | tail -1)"
fi
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> "$CLAUDE_ENV_FILE"
fi

# 2. npm dependencies for every Vite client -----------------------------------
while IFS= read -r pkg; do
  dir="$(dirname "$pkg")"
  if [ -f "$dir/package-lock.json" ]; then
    # npm ci honours the lockfile exactly and never rewrites it (npm install does).
    log "npm ci in ${dir#"$ROOT"/}"
    npm --prefix "$dir" ci --no-audit --no-fund --loglevel=error
  else
    log "npm install in ${dir#"$ROOT"/}"
    npm --prefix "$dir" install --no-audit --no-fund --loglevel=error
  fi
done < <(find "$ROOT" -maxdepth 3 -path '*/ClientApp/package.json' -not -path '*/node_modules/*')

# 3. NuGet restore -----------------------------------------------------------
if [ -f "$ROOT/Wps.Watch.AiReporting.sln" ]; then
  log "dotnet restore"
  dotnet restore "$ROOT/Wps.Watch.AiReporting.sln" --verbosity quiet
fi

log "done"
