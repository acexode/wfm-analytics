-- Apply after migrations using the migration account. The WP02 runtime is read-only.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA platform, analytics TO wfm_app;
GRANT SELECT ON platform.schema_migrations, platform.access_grants,
    analytics.synthetic_daily_reports TO wfm_app;
