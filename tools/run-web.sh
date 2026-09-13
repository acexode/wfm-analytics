#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/dev-env.sh"
cd "$WFM_ROOT"
exec npm --prefix src/web run dev
