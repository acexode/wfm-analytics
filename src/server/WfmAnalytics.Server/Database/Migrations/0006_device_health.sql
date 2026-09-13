CREATE TABLE IF NOT EXISTS ingestion.device_health_reports
(
    enrollment_id uuid NOT NULL,
    reported_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    checked_at timestamptz NOT NULL,
    agent_version text NOT NULL,
    queue_pending_payload_count integer NOT NULL,
    queue_pending_payload_bytes bigint NOT NULL,
    queue_loss_record_count integer NOT NULL,
    queue_lost_payload_count integer NOT NULL,
    session_state text,
    warnings text[] NOT NULL,
    payload jsonb NOT NULL,
    CONSTRAINT device_health_agent_version_not_blank CHECK (btrim(agent_version) <> ''),
    CONSTRAINT device_health_queue_counts_nonnegative CHECK (
        queue_pending_payload_count >= 0
        AND queue_pending_payload_bytes >= 0
        AND queue_loss_record_count >= 0
        AND queue_lost_payload_count >= 0
    )
);

CREATE INDEX IF NOT EXISTS ix_device_health_reports_enrollment_reported
    ON ingestion.device_health_reports(enrollment_id, reported_at DESC);
