# BPO Workforce Operations Analytics

An internal, cost-conscious operations analytics project. The accepted baseline and the first executable vertical slice are now present: strict data contracts, an ASP.NET Core API backed by PostgreSQL, and a React daily operations dashboard using synthetic data.

## Accepted planning baseline

1. [Project definition](docs/01-project-definition.md): objectives, users, scope, workflows, metric definitions, and pilot acceptance.
2. [Technical implementation plan](docs/02-technical-implementation-plan.md): PostgreSQL-only architecture, Windows collection, data model, APIs, costs, testing, and milestones.
3. [AI delivery model](docs/03-agent-delivery-model.md): lead ownership, specialist roles, independent review, work packages, costs, and escalation boundaries.
4. [Status and decisions](docs/04-project-status-and-decisions.md): current state, accepted decisions, and dependencies.

The consolidated reading copy is [Project Blueprint](output/pdf/BPO_Workforce_Analytics_Project_Blueprint.pdf). The three numbered source documents are authoritative; regenerate the PDF when they change using tools/build_blueprint.py with the bundled Python runtime.

Original PDFs in the project root are unchanged discovery inputs. Files under docs/reviews are specialist contributions, not competing implementation plans. The lead resolves them into the baseline above.

Default stack: Windows C# collector, ASP.NET Core API and worker, React and TypeScript interface, and self-hosted PostgreSQL. No ClickHouse or paid runtime AI service is planned for the first release.

## Run the synthetic vertical slice

Prerequisites are .NET 10, Node.js 22.12 or newer, Python 3.12 with `jsonschema`, and a local PostgreSQL installation. This workspace also detects its project-local .NET SDK and Codex bundled Node.js runtime when present.

```sh
bash tools/local-db.sh start
bash tools/prepare-demo.sh
```

Then run the API and web interface in separate terminals:

```sh
bash tools/run-api.sh
bash tools/run-web.sh
```

Open `http://127.0.0.1:5173`. The development proxy supplies the fixed synthetic manager identity; it does not accept browser-provided authorization scope. Stop the isolated database with `bash tools/local-db.sh stop`.

Run all contract, server/database, and web checks with the database started:

```sh
bash tools/verify.sh
```

The Windows collector, ingestion endpoint, production identity, operational imports, production aggregation, CI, and employee-device deployment remain later work packages. Real Windows behavior can only pass its gate from tests on Windows.
