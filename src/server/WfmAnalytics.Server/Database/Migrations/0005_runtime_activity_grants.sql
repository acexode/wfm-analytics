DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wfm_app') THEN
        GRANT USAGE ON SCHEMA platform, ingestion, analytics TO wfm_app;
        GRANT SELECT ON platform.access_grants, platform.development_enrollments TO wfm_app;
        GRANT SELECT, INSERT, UPDATE, DELETE ON ingestion.ingestion_receipts, ingestion.activity_envelopes, ingestion.activity_dirty_days TO wfm_app;
        GRANT SELECT, INSERT, UPDATE ON analytics.activity_daily_reports TO wfm_app;
        GRANT SELECT ON analytics.synthetic_daily_reports TO wfm_app;
    END IF;
END $$;
