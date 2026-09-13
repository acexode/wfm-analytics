#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/dev-env.sh"
cd "$WFM_ROOT"
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://127.0.0.1:5050
exec dotnet run --no-build --no-launch-profile --project src/server/WfmAnalytics.Server "$@"
