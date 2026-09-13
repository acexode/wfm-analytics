#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/dev-env.sh"
cd "$WFM_ROOT"
: "${ConnectionStrings__Migration:?Start the isolated database first.}"
export ASPNETCORE_ENVIRONMENT=Development
export SeedFixture="$WFM_ROOT/fixtures/daily-report.json"
dotnet run --project src/server/WfmAnalytics.Server -- --migrate
dotnet run --no-build --project src/server/WfmAnalytics.Server -- --seed-development
bash tools/local-db.sh grant
