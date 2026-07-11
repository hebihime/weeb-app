CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'identity') THEN
            CREATE SCHEMA identity;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE TABLE identity.accounts (
        account_id text NOT NULL,
        handle text NOT NULL,
        email text,
        email_verified_at timestamp with time zone NOT NULL,
        birthdate_enc bytea NOT NULL,
        attested_adult_at timestamp with time zone NOT NULL,
        terms_version text NOT NULL,
        fandom_tag text NOT NULL,
        avatar_ref text,
        locale text NOT NULL,
        account_state text NOT NULL DEFAULT 'active',
        irl_access_state text NOT NULL DEFAULT 'active',
        state_changed_at timestamp with time zone NOT NULL,
        deletion_requested_at timestamp with time zone,
        deletion_effective_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        last_active_at timestamp with time zone NOT NULL,
        tombstoned_at timestamp with time zone,
        region text NOT NULL,
        region_source text NOT NULL,
        lawful_basis text NOT NULL,
        CONSTRAINT "PK_accounts" PRIMARY KEY (account_id),
        CONSTRAINT ck_accounts_state CHECK (account_state IN ('active','suspended','banned','deleted'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE TABLE identity.ban_evasion_refs (
        hmac_email text NOT NULL,
        push_token_hash bytea,
        banned_at timestamp with time zone NOT NULL,
        region text NOT NULL,
        lawful_basis text NOT NULL,
        CONSTRAINT "PK_ban_evasion_refs" PRIMARY KEY (hmac_email)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE TABLE identity.consent_current (
        account_id text NOT NULL,
        consent_kind text NOT NULL,
        version text NOT NULL,
        status text NOT NULL,
        surface text NOT NULL,
        decided_at timestamp with time zone NOT NULL,
        region text NOT NULL,
        lawful_basis text NOT NULL,
        CONSTRAINT "PK_consent_current" PRIMARY KEY (account_id, consent_kind),
        CONSTRAINT ck_consent_current_status CHECK (status IN ('granted','revoked'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE TABLE identity.deletion_jobs (
        deletion_id text NOT NULL,
        account_id text NOT NULL,
        state text NOT NULL,
        requested_at timestamp with time zone NOT NULL,
        scheduled_for timestamp with time zone NOT NULL,
        export_offered boolean NOT NULL DEFAULT TRUE,
        custody_holds_found integer,
        custody_hold_refs jsonb,
        executed_at timestamp with time zone,
        purge_run_ids jsonb,
        region text NOT NULL,
        lawful_basis text NOT NULL,
        CONSTRAINT "PK_deletion_jobs" PRIMARY KEY (deletion_id),
        CONSTRAINT ck_deletion_jobs_state CHECK (state IN ('scheduled','canceled','executing','held','complete'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE TABLE identity.devices (
        device_id text NOT NULL,
        account_id text NOT NULL,
        platform text NOT NULL,
        push_token text,
        push_token_updated_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        last_seen_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        region text NOT NULL,
        lawful_basis text NOT NULL,
        CONSTRAINT "PK_devices" PRIMARY KEY (device_id),
        CONSTRAINT ck_devices_platform CHECK (platform IN ('ios','android','web'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE TABLE identity.email_challenges (
        challenge_id text NOT NULL,
        purpose text NOT NULL,
        email_lower text NOT NULL,
        account_id text,
        code_hash bytea NOT NULL,
        attempts integer NOT NULL DEFAULT 0,
        verified_at timestamp with time zone,
        verified_token_hash bytea,
        consumed_at timestamp with time zone,
        expires_at timestamp with time zone NOT NULL,
        created_at timestamp with time zone NOT NULL,
        locale text NOT NULL,
        region text NOT NULL,
        lawful_basis text NOT NULL,
        CONSTRAINT "PK_email_challenges" PRIMARY KEY (challenge_id),
        CONSTRAINT ck_email_challenges_purpose CHECK (purpose IN ('signup','login','email_change'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE TABLE identity.export_jobs (
        export_id text NOT NULL,
        account_id text NOT NULL,
        state text NOT NULL,
        artifact bytea,
        manifest jsonb,
        requested_at timestamp with time zone NOT NULL,
        ready_at timestamp with time zone,
        expires_at timestamp with time zone,
        region text NOT NULL,
        lawful_basis text NOT NULL,
        CONSTRAINT "PK_export_jobs" PRIMARY KEY (export_id),
        CONSTRAINT ck_export_jobs_state CHECK (state IN ('pending','ready','delivered','expired','failed'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE TABLE identity.handle_history (
        id text NOT NULL,
        account_id text NOT NULL,
        old_handle text NOT NULL,
        new_handle text NOT NULL,
        changed_at timestamp with time zone NOT NULL,
        region text NOT NULL,
        lawful_basis text NOT NULL,
        CONSTRAINT "PK_handle_history" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE TABLE identity.push_category_consents (
        account_id text NOT NULL,
        category smallint NOT NULL,
        enabled boolean NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        region text NOT NULL,
        lawful_basis text NOT NULL,
        CONSTRAINT "PK_push_category_consents" PRIMARY KEY (account_id, category),
        CONSTRAINT ck_push_category_consents_category CHECK (category BETWEEN 1 AND 9 AND category <> 8)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE TABLE identity.refresh_tokens (
        id text NOT NULL,
        session_id text NOT NULL,
        token_hash bytea NOT NULL,
        family_id text NOT NULL,
        issued_at timestamp with time zone NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        consumed_at timestamp with time zone,
        superseded_by text,
        region text NOT NULL,
        lawful_basis text NOT NULL,
        CONSTRAINT "PK_refresh_tokens" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE TABLE identity.reserved_handles (
        handle text NOT NULL,
        reason text NOT NULL,
        CONSTRAINT "PK_reserved_handles" PRIMARY KEY (handle)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE TABLE identity.retired_handles (
        handle text NOT NULL,
        retired_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_retired_handles" PRIMARY KEY (handle)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE TABLE identity.sessions (
        session_id text NOT NULL,
        account_id text NOT NULL,
        device_id text,
        access_token_hash bytea NOT NULL,
        refresh_family_id text NOT NULL,
        created_at timestamp with time zone NOT NULL,
        last_seen_at timestamp with time zone NOT NULL,
        access_expires_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        revoke_reason text,
        region text NOT NULL,
        lawful_basis text NOT NULL,
        CONSTRAINT "PK_sessions" PRIMARY KEY (session_id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE INDEX ix_accounts_deletion_due ON identity.accounts (deletion_effective_at) WHERE deletion_effective_at IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE UNIQUE INDEX ux_accounts_email ON identity.accounts (email) WHERE account_state <> 'deleted' AND email IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE UNIQUE INDEX ux_accounts_handle ON identity.accounts (handle) WHERE account_state <> 'deleted';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE INDEX ix_deletion_jobs_account ON identity.deletion_jobs (account_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE INDEX ix_devices_account ON identity.devices (account_id) WHERE revoked_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE INDEX ix_email_challenges_account ON identity.email_challenges (account_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE INDEX ix_email_challenges_email_lower ON identity.email_challenges (email_lower);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE UNIQUE INDEX ux_export_active ON identity.export_jobs (account_id) WHERE state IN ('pending','ready');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE INDEX ix_handle_history_account ON identity.handle_history (account_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE INDEX ix_refresh_tokens_family ON identity.refresh_tokens (family_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE UNIQUE INDEX ux_refresh_tokens_token_hash ON identity.refresh_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE INDEX ix_sessions_account ON identity.sessions (account_id) WHERE revoked_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    CREATE UNIQUE INDEX ux_sessions_access_token_hash ON identity.sessions (access_token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260711145012_InitialIdentity') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260711145012_InitialIdentity', '10.0.9');
    END IF;
END $EF$;
COMMIT;

