CREATE SCHEMA analytics;
CREATE TABLE platform.access_grants (
    principal_id text NOT NULL,
    team_id text NOT NULL,
    permission text NOT NULL,
    valid_from timestamptz NOT NULL,
    valid_until timestamptz,
    PRIMARY KEY (principal_id, team_id, permission),
    CHECK (valid_until IS NULL OR valid_until > valid_from)
);
-- WP02 synthetic read model only; WP04 owns source ingestion and recomputation.
CREATE TABLE analytics.synthetic_daily_reports (
    team_id text NOT NULL,
    report_date date NOT NULL,
    payload jsonb NOT NULL,
    PRIMARY KEY (team_id, report_date),
    CHECK (payload->>'synthetic' = 'true'),
    CHECK (payload->>'team_id' = team_id),
    CHECK ((payload->>'report_date')::date = report_date)
);
