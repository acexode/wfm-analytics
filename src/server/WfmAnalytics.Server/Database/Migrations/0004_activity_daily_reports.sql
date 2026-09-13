CREATE TABLE IF NOT EXISTS platform.development_enrollments
(
    enrollment_id uuid PRIMARY KEY,
    team_id text NOT NULL,
    team_name text NOT NULL,
    employee_id text NOT NULL,
    display_name text NOT NULL,
    timezone text NOT NULL DEFAULT 'UTC',
    CHECK (btrim(team_id) <> ''),
    CHECK (btrim(team_name) <> ''),
    CHECK (btrim(employee_id) <> ''),
    CHECK (btrim(display_name) <> ''),
    CHECK (btrim(timezone) <> '')
);

INSERT INTO platform.development_enrollments(enrollment_id,team_id,team_name,employee_id,display_name,timezone)
VALUES ('dddddddd-dddd-4ddd-8ddd-dddddddddddd','team-synthetic','Synthetic Operations','employee-live-test','Live Windows test employee','UTC')
ON CONFLICT (enrollment_id) DO UPDATE
SET team_id=EXCLUDED.team_id,
    team_name=EXCLUDED.team_name,
    employee_id=EXCLUDED.employee_id,
    display_name=EXCLUDED.display_name,
    timezone=EXCLUDED.timezone;

CREATE TABLE IF NOT EXISTS ingestion.activity_dirty_days
(
    enrollment_id uuid NOT NULL,
    report_date date NOT NULL,
    dirty_generation bigint NOT NULL DEFAULT 1,
    last_event_end timestamptz NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (enrollment_id, report_date),
    CHECK (dirty_generation >= 1)
);

CREATE TABLE IF NOT EXISTS analytics.activity_daily_reports
(
    team_id text NOT NULL,
    report_date date NOT NULL,
    payload jsonb NOT NULL,
    report_revision bigint NOT NULL DEFAULT 1,
    computed_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (team_id, report_date),
    CHECK (payload->>'schema_version' = '1.0'),
    CHECK (payload->>'team_id' = team_id),
    CHECK ((payload->>'report_date')::date = report_date)
);
