#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/dev-env.sh"
cd "$WFM_ROOT"
: "${WFM_TEST_DATABASE:?Set WFM_TEST_DATABASE to the isolated wfm_test database or start tools/local-db.sh first.}"
"${WFM_PYTHON:-python3}" -m unittest discover -s tests/contracts -v
dotnet build src/server/WfmAnalytics.Server.slnx --no-restore
dotnet run --no-build --project tests/server/WfmAnalytics.Server.Tests
npm --prefix src/web test
npm --prefix src/web run build
