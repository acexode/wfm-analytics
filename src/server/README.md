# Server development foundation

This directory contains the ASP.NET Core modular-monolith foundation for the internal workforce analytics application. It targets .NET 10 LTS and uses Npgsql with PostgreSQL. No paid service or runtime AI dependency is required.

## Local setup

From the repository root, start and prepare the isolated development database, then run the checks or API:

```sh
bash tools/local-db.sh start
bash tools/prepare-demo.sh
bash tools/verify.sh
bash tools/run-api.sh
```

The service exposes `GET /health`, `GET /health/ready`, the protected demonstration route `GET /api/demo/scoped`, and the synthetic report route `GET /api/v1/teams/{teamId}/daily?date=YYYY-MM-DD`.

In `Development` only, a synthetic identity can be supplied for local requests:

```sh
curl -H 'X-Development-Principal: demo-manager' \
  http://127.0.0.1:5050/api/demo/scoped
```

These headers are ignored outside `Development`. The named development principal has fixed server-side claims; the server ignores requested scopes. Production identity remains deliberately unconfigured until the company's identity provider is selected.

## PostgreSQL migrations

SQL migrations are embedded resources under `WfmAnalytics.Server/Database/Migrations`. The migration catalog validates ordered, unique versions and SHA-256 checksums. Migration `0001` creates the ledger; migration `0002` creates development access grants and the synthetic daily report store.

The runtime database role cannot apply migrations. `--migrate` uses the separately configured migration connection and records each committed checksum in `platform.schema_migrations`; the normal API startup never applies migrations. Set connection strings outside source control. `/health/ready` opens PostgreSQL and rejects missing or changed migrations.
