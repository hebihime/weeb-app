CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260712030839_InitialAdmin') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'admin') THEN
            CREATE SCHEMA admin;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260712030839_InitialAdmin') THEN
    CREATE TABLE admin.staff_accounts (
        id text NOT NULL,
        external_subject text NOT NULL,
        email text NOT NULL,
        display_name text NOT NULL,
        status text NOT NULL,
        security_stamp text NOT NULL,
        region text NOT NULL,
        lawful_basis text NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        deactivated_at timestamp with time zone,
        CONSTRAINT "PK_staff_accounts" PRIMARY KEY (id),
        CONSTRAINT ck_staff_accounts_status CHECK (status IN ('active','deactivated'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260712030839_InitialAdmin') THEN
    CREATE TABLE admin.staff_role_grants (
        id text NOT NULL,
        staff_id text NOT NULL,
        role text NOT NULL,
        granted_by text NOT NULL,
        grant_reason text NOT NULL,
        granted_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        revoked_by text,
        revoke_reason text,
        region text NOT NULL,
        lawful_basis text NOT NULL,
        CONSTRAINT "PK_staff_role_grants" PRIMARY KEY (id),
        CONSTRAINT ck_staff_role_grants_role CHECK (role IN ('super_admin','safety_agent','content_moderator','venue_con_ops','economy_ops','analyst')),
        CONSTRAINT "FK_staff_role_grants_staff_accounts_staff_id" FOREIGN KEY (staff_id) REFERENCES admin.staff_accounts (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260712030839_InitialAdmin') THEN
    CREATE UNIQUE INDEX ux_staff_accounts_email ON admin.staff_accounts (email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260712030839_InitialAdmin') THEN
    CREATE UNIQUE INDEX ux_staff_accounts_external_subject ON admin.staff_accounts (external_subject);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260712030839_InitialAdmin') THEN
    CREATE UNIQUE INDEX ux_active_grant ON admin.staff_role_grants (staff_id, role) WHERE revoked_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260712030839_InitialAdmin') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260712030839_InitialAdmin', '10.0.9');
    END IF;
END $EF$;
COMMIT;

