CREATE SCHEMA IF NOT EXISTS ingestion;

CREATE TABLE IF NOT EXISTS ingestion.ingestion_receipts
(
    enrollment_id uuid NOT NULL,
    event_id uuid NOT NULL,
    collector_instance_id uuid NOT NULL,
    sequence bigint NOT NULL,
    payload_checksum character(64) NOT NULL,
    first_accepted_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    receipt_expires_at timestamptz NOT NULL,
    PRIMARY KEY (enrollment_id, event_id),
    CONSTRAINT ingestion_receipts_checksum_format CHECK (payload_checksum ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ingestion_receipts_sequence_nonnegative CHECK (sequence >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ingestion_receipts_enrollment_instance_sequence
    ON ingestion.ingestion_receipts(enrollment_id, collector_instance_id, sequence);

CREATE TABLE IF NOT EXISTS ingestion.activity_envelopes
(
    enrollment_id uuid NOT NULL,
    event_id uuid NOT NULL,
    boot_id text NOT NULL,
    session_id text NOT NULL,
    collector_instance_id uuid NOT NULL,
    sequence bigint NOT NULL,
    bucket_start timestamptz NOT NULL,
    bucket_end timestamptz NOT NULL,
    coarsened boolean NOT NULL,
    payload jsonb NOT NULL,
    payload_checksum character(64) NOT NULL,
    received_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (enrollment_id, event_id),
    CONSTRAINT activity_envelopes_boot_id_not_blank CHECK (btrim(boot_id) <> ''),
    CONSTRAINT activity_envelopes_session_id_not_blank CHECK (btrim(session_id) <> ''),
    CONSTRAINT activity_envelopes_bucket_order CHECK (bucket_end > bucket_start),
    CONSTRAINT activity_envelopes_sequence_nonnegative CHECK (sequence >= 0),
    CONSTRAINT activity_envelopes_checksum_format CHECK (payload_checksum ~ '^[0-9a-f]{64}$')
);

CREATE INDEX IF NOT EXISTS ix_activity_envelopes_bucket_start
    ON ingestion.activity_envelopes(bucket_start);
