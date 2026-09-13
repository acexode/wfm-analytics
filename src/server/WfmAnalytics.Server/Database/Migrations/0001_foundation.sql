CREATE SCHEMA IF NOT EXISTS platform;

CREATE TABLE IF NOT EXISTS platform.schema_migrations
(
    version bigint PRIMARY KEY,
    name text NOT NULL,
    sha256 character(64) NOT NULL,
    applied_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT schema_migrations_name_not_blank CHECK (btrim(name) <> ''),
    CONSTRAINT schema_migrations_sha256_format CHECK (sha256 ~ '^[0-9a-f]{64}$')
);
