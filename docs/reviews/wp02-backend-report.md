# WP02 backend foundation report

Status: accepted as a synthetic development foundation on 13 September 2026

The package supplies a .NET 10 ASP.NET Core modular monolith, Npgsql PostgreSQL access, checksum-protected ordered migrations, an explicit migration command, development-only synthetic seeding, liveness and database readiness routes, a scoped synthetic route, and a PostgreSQL-backed daily report route. It adds no paid service or runtime AI dependency.

Development identity recognizes only the fixed `demo-manager` and `demo-unscoped` principals. The server assigns their claims; request headers cannot invent scopes. Development headers are ignored in Production. Database authorization verifies the manager's team grant before reading a report. Production OIDC remains deliberately absent until the company identity provider is selected.

The lead supplied an isolated local PostgreSQL cluster on `127.0.0.1:55432`, separate migration and runtime roles, ignored random local credentials, runtime grants, and repeatable start/prepare/run scripts. Migrations and the synthetic seed are idempotent. The runtime role cannot apply migrations. The synthetic daily response preserves unavailable output as null with its reason.

Actual checks passed with .NET SDK 10.0.401 and PostgreSQL 14.2:

- solution build with no warnings or errors;
- anonymous liveness;
- authentication and authorization cases for 401, 403, allowed scope, unknown principal, and Production rejection of development headers;
- readiness failure for an unavailable database and readiness success only when migration versions and checksums match;
- repeat migration and repeat synthetic seed;
- daily report success, cross-team denial, missing date, and invalid date;
- deliberate migration checksum corruption rejected and restored;
- Production synthetic seed rejected.

Limitations: this is a development identity and a precomputed synthetic report store. Collector ingestion, import staging, production aggregation, production identity, audit export, backup/restore, CI, load evidence, and deployment remain later packages. No Windows behavior is claimed.
