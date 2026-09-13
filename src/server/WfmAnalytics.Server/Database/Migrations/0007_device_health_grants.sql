DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wfm_app') THEN
        GRANT SELECT, INSERT ON ingestion.device_health_reports TO wfm_app;
    END IF;
END $$;
