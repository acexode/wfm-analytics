#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/dev-env.sh"
cd "$WFM_ROOT"
action="${1:-start}"
case "$action" in start|stop|status|grant) ;; *) echo 'Usage: bash tools/local-db.sh start|stop|status|grant' >&2; exit 2;; esac
if [[ -z "${PG_BIN:-}" ]]; then
  if command -v pg_ctl >/dev/null 2>&1; then PG_BIN="$(dirname "$(command -v pg_ctl)")";
  elif [[ -x /Library/PostgreSQL/14/bin/pg_ctl ]]; then PG_BIN=/Library/PostgreSQL/14/bin;
  else echo 'Set PG_BIN to an installed PostgreSQL bin directory, or use compose.yaml.' >&2; exit 1; fi
fi
data="$WFM_ROOT/.local/pgdata"
if [[ "$action" == stop || "$action" == status ]]; then
  "$PG_BIN/pg_ctl" -D "$data" "$action"
  exit
fi
node tools/local-env.mjs
source tools/dev-env.sh
export PGPASSWORD="$WFM_MIGRATION_PASSWORD"
if [[ "$action" == start ]]; then
  if [[ ! -f "$data/PG_VERSION" ]]; then
    "$PG_BIN/initdb" -D "$data" --username=wfm_migrator --pwfile="$WFM_ROOT/.local/migration-password" --auth-local=trust --auth-host=scram-sha-256 --encoding=UTF8 --locale=C
  fi
  if ! "$PG_BIN/pg_ctl" -D "$data" status >/dev/null 2>&1; then
    "$PG_BIN/pg_ctl" -D "$data" -l "$WFM_ROOT/.local/postgres.log" -o "-h 127.0.0.1 -p 55432 -k ''" -w start
  fi
  # Connect only to our explicitly bound cluster, never a system default socket.
  "$PG_BIN/psql" -X -h 127.0.0.1 -p 55432 -U wfm_migrator -d postgres -v ON_ERROR_STOP=1 -v app_password="$WFM_APP_PASSWORD" <<'SQL'
SELECT 'CREATE DATABASE wfm_dev' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'wfm_dev') \gexec
SELECT 'CREATE DATABASE wfm_test' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'wfm_test') \gexec
SELECT format('CREATE ROLE wfm_app LOGIN PASSWORD %L', :'app_password') WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'wfm_app') \gexec
REVOKE ALL ON DATABASE wfm_dev FROM PUBLIC;
REVOKE ALL ON DATABASE wfm_test FROM PUBLIC;
GRANT CONNECT ON DATABASE wfm_dev TO wfm_app;
SQL
  echo 'Isolated development PostgreSQL is ready on 127.0.0.1:55432.'
else
  "$PG_BIN/psql" -X -h 127.0.0.1 -p 55432 -U wfm_migrator -d wfm_dev -v ON_ERROR_STOP=1 -f tools/runtime-grants.sql
fi
