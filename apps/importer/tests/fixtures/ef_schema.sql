CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE EXTENSION IF NOT EXISTS postgis;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE TABLE asset_type (
        id uuid NOT NULL,
        key text NOT NULL,
        name text NOT NULL,
        icon text NOT NULL,
        attribute_schema jsonb NOT NULL,
        CONSTRAINT pk_asset_type PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE TABLE data_source (
        id uuid NOT NULL,
        key text NOT NULL,
        adapter_key text NOT NULL,
        name text NOT NULL,
        city text NOT NULL,
        source_url text NOT NULL,
        license text NOT NULL,
        attribution text NOT NULL,
        config jsonb NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_data_source PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE TABLE outbox_message (
        id uuid NOT NULL,
        type character varying(128) NOT NULL,
        payload jsonb NOT NULL,
        occurred_at timestamp with time zone NOT NULL,
        available_at timestamp with time zone NOT NULL,
        processed_at timestamp with time zone,
        attempts integer NOT NULL,
        last_error text,
        status character varying(24) NOT NULL,
        CONSTRAINT pk_outbox_message PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE TABLE task_type (
        id uuid NOT NULL,
        key text NOT NULL,
        name text NOT NULL,
        config_schema jsonb NOT NULL,
        result_schema jsonb NOT NULL,
        CONSTRAINT pk_task_type PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE TABLE "user" (
        id uuid NOT NULL,
        username character varying(64) NOT NULL,
        password_hash text NOT NULL,
        role character varying(24) NOT NULL,
        display_name character varying(64),
        locale character varying(8) NOT NULL,
        total_points integer NOT NULL,
        last_login_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        deleted_at timestamp with time zone,
        CONSTRAINT pk_user PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE TABLE asset (
        id uuid NOT NULL,
        asset_type_id uuid NOT NULL,
        data_source_id uuid NOT NULL,
        external_id text NOT NULL,
        geom geography (point) NOT NULL,
        attributes jsonb NOT NULL,
        raw jsonb NOT NULL,
        source_hash character varying(64) NOT NULL,
        status character varying(24) NOT NULL,
        first_seen_at timestamp with time zone NOT NULL,
        last_seen_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_asset PRIMARY KEY (id),
        CONSTRAINT fk_asset_asset_type_asset_type_id FOREIGN KEY (asset_type_id) REFERENCES asset_type (id) ON DELETE CASCADE,
        CONSTRAINT fk_asset_data_source_data_source_id FOREIGN KEY (data_source_id) REFERENCES data_source (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE TABLE sync_run (
        id uuid NOT NULL,
        data_source_id uuid NOT NULL,
        started_at timestamp with time zone NOT NULL,
        finished_at timestamp with time zone,
        status text NOT NULL,
        assets_created integer NOT NULL,
        assets_updated integer NOT NULL,
        assets_removed integer NOT NULL,
        error text,
        CONSTRAINT pk_sync_run PRIMARY KEY (id),
        CONSTRAINT fk_sync_run_data_source_data_source_id FOREIGN KEY (data_source_id) REFERENCES data_source (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE TABLE asset_type_task_type (
        asset_type_id uuid NOT NULL,
        task_type_id uuid NOT NULL,
        CONSTRAINT pk_asset_type_task_type PRIMARY KEY (asset_type_id, task_type_id),
        CONSTRAINT fk_asset_type_task_type_asset_type_asset_type_id FOREIGN KEY (asset_type_id) REFERENCES asset_type (id) ON DELETE CASCADE,
        CONSTRAINT fk_asset_type_task_type_task_type_task_type_id FOREIGN KEY (task_type_id) REFERENCES task_type (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE TABLE export_run (
        id uuid NOT NULL,
        data_source_id uuid NOT NULL,
        created_by uuid,
        format text NOT NULL,
        storage_key text,
        status text NOT NULL,
        change_count integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_export_run PRIMARY KEY (id),
        CONSTRAINT fk_export_run_data_source_data_source_id FOREIGN KEY (data_source_id) REFERENCES data_source (id) ON DELETE CASCADE,
        CONSTRAINT fk_export_run_user_created_by FOREIGN KEY (created_by) REFERENCES "user" (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE TABLE quest_campaign (
        id uuid NOT NULL,
        created_by uuid NOT NULL,
        title text NOT NULL,
        description text,
        asset_filter jsonb NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_quest_campaign PRIMARY KEY (id),
        CONSTRAINT fk_quest_campaign_user_created_by FOREIGN KEY (created_by) REFERENCES "user" (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE TABLE user_recovery_code (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        code_hash text NOT NULL,
        used_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_user_recovery_code PRIMARY KEY (id),
        CONSTRAINT fk_user_recovery_code_user_user_id FOREIGN KEY (user_id) REFERENCES "user" (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE TABLE quest (
        id uuid NOT NULL,
        campaign_id uuid,
        asset_id uuid NOT NULL,
        task_type_id uuid NOT NULL,
        created_by uuid NOT NULL,
        title text NOT NULL,
        description text,
        task_config jsonb NOT NULL,
        max_completions integer NOT NULL,
        slots_taken integer NOT NULL,
        reward_points integer NOT NULL,
        geofence_radius_m integer NOT NULL,
        claim_ttl_minutes integer NOT NULL,
        status character varying(24) NOT NULL,
        starts_at timestamp with time zone,
        ends_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_quest PRIMARY KEY (id),
        CONSTRAINT ck_quest_slots CHECK (slots_taken >= 0 AND max_completions >= 1),
        CONSTRAINT fk_quest_asset_asset_id FOREIGN KEY (asset_id) REFERENCES asset (id) ON DELETE CASCADE,
        CONSTRAINT fk_quest_quest_campaign_campaign_id FOREIGN KEY (campaign_id) REFERENCES quest_campaign (id),
        CONSTRAINT fk_quest_task_type_task_type_id FOREIGN KEY (task_type_id) REFERENCES task_type (id) ON DELETE CASCADE,
        CONSTRAINT fk_quest_user_created_by FOREIGN KEY (created_by) REFERENCES "user" (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE TABLE claim (
        id uuid NOT NULL,
        quest_id uuid NOT NULL,
        user_id uuid NOT NULL,
        status character varying(24) NOT NULL,
        claimed_at timestamp with time zone NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        closed_at timestamp with time zone,
        CONSTRAINT pk_claim PRIMARY KEY (id),
        CONSTRAINT fk_claim_quest_quest_id FOREIGN KEY (quest_id) REFERENCES quest (id) ON DELETE CASCADE,
        CONSTRAINT fk_claim_user_user_id FOREIGN KEY (user_id) REFERENCES "user" (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE TABLE submission (
        id uuid NOT NULL,
        claim_id uuid NOT NULL,
        payload jsonb NOT NULL,
        location geography (point) NOT NULL,
        distance_m double precision NOT NULL,
        status character varying(24) NOT NULL,
        reviewed_by uuid,
        reviewed_at timestamp with time zone,
        rejection_reason text,
        submitted_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_submission PRIMARY KEY (id),
        CONSTRAINT fk_submission_claim_claim_id FOREIGN KEY (claim_id) REFERENCES claim (id) ON DELETE CASCADE,
        CONSTRAINT fk_submission_user_reviewed_by FOREIGN KEY (reviewed_by) REFERENCES "user" (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE TABLE attribute_change (
        id uuid NOT NULL,
        submission_id uuid NOT NULL,
        asset_id uuid NOT NULL,
        attribute_key character varying(64) NOT NULL,
        old_value jsonb,
        new_value jsonb,
        status character varying(24) NOT NULL,
        export_run_id uuid,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_attribute_change PRIMARY KEY (id),
        CONSTRAINT fk_attribute_change_asset_asset_id FOREIGN KEY (asset_id) REFERENCES asset (id) ON DELETE CASCADE,
        CONSTRAINT fk_attribute_change_export_runs_export_run_id FOREIGN KEY (export_run_id) REFERENCES export_run (id),
        CONSTRAINT fk_attribute_change_submission_submission_id FOREIGN KEY (submission_id) REFERENCES submission (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE TABLE media (
        id uuid NOT NULL,
        submission_id uuid NOT NULL,
        storage_key character varying(256) NOT NULL,
        mime_type text NOT NULL,
        width integer NOT NULL,
        height integer NOT NULL,
        size_bytes integer NOT NULL,
        sha256 character varying(64) NOT NULL,
        phash character varying(16) NOT NULL,
        captured_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_media PRIMARY KEY (id),
        CONSTRAINT fk_media_submissions_submission_id FOREIGN KEY (submission_id) REFERENCES submission (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_asset_asset_type_id_status ON asset (asset_type_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_asset_attributes ON asset USING gin (attributes jsonb_path_ops);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_asset_data_source_id_external_id ON asset (data_source_id, external_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_asset_geom ON asset USING gist (geom);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_asset_type_key ON asset_type (key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_asset_type_task_type_task_type_id ON asset_type_task_type (task_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_attribute_change_asset_id ON attribute_change (asset_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_attribute_change_export_run_id ON attribute_change (export_run_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_attribute_change_status ON attribute_change (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_attribute_change_submission_id ON attribute_change (submission_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_claim_expires_at ON claim (expires_at) WHERE status = 'active';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_claim_quest_id_status ON claim (quest_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_claim_quest_id_user_id ON claim (quest_id, user_id) WHERE status IN ('active','submitted');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_claim_user_id_status ON claim (user_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_data_source_key ON data_source (key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_export_run_created_by ON export_run (created_by);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_export_run_data_source_id ON export_run (data_source_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_media_sha256 ON media (sha256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_media_submission_id ON media (submission_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_outbox_message_available_at_occurred_at ON outbox_message (available_at, occurred_at) WHERE status = 'pending';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_quest_asset_id ON quest (asset_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_quest_campaign_id ON quest (campaign_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_quest_created_by ON quest (created_by);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_quest_status_asset_id ON quest (status, asset_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_quest_task_type_id ON quest (task_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_quest_campaign_created_by ON quest_campaign (created_by);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_submission_claim_id ON submission (claim_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_submission_reviewed_by ON submission (reviewed_by);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_submission_status_submitted_at ON submission (status, submitted_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_sync_run_data_source_id_started_at ON sync_run (data_source_id, started_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_task_type_key ON task_type (key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_user_username ON "user" (username);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    CREATE INDEX ix_user_recovery_code_user_id ON user_recovery_code (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN

                    CREATE FUNCTION notify_outbox() RETURNS trigger AS $$
                    BEGIN
                        PERFORM pg_notify('outbox', NEW.id::text);
                        RETURN NEW;
                    END;
                    $$ LANGUAGE plpgsql;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN

                    CREATE TRIGGER outbox_message_notify AFTER INSERT ON outbox_message
                    FOR EACH ROW EXECUTE FUNCTION notify_outbox();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925143444_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260925143444_InitialCreate', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925152920_AddAssetSnapshots') THEN
    ALTER TABLE sync_run ADD record_count integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925152920_AddAssetSnapshots') THEN
    ALTER TABLE sync_run ADD schema_hash character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925152920_AddAssetSnapshots') THEN
    ALTER TABLE sync_run ADD snapshot_key character varying(256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925152920_AddAssetSnapshots') THEN
    CREATE TABLE asset_snapshot (
        id uuid NOT NULL,
        asset_id uuid NOT NULL,
        sync_run_id uuid NOT NULL,
        change_type character varying(24) NOT NULL,
        geom geography (point) NOT NULL,
        raw jsonb NOT NULL,
        source_hash character varying(64) NOT NULL,
        CONSTRAINT pk_asset_snapshot PRIMARY KEY (id),
        CONSTRAINT fk_asset_snapshot_assets_asset_id FOREIGN KEY (asset_id) REFERENCES asset (id) ON DELETE CASCADE,
        CONSTRAINT fk_asset_snapshot_sync_run_sync_run_id FOREIGN KEY (sync_run_id) REFERENCES sync_run (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925152920_AddAssetSnapshots') THEN
    CREATE UNIQUE INDEX ix_asset_snapshot_asset_id_sync_run_id ON asset_snapshot (asset_id, sync_run_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925152920_AddAssetSnapshots') THEN
    CREATE INDEX ix_asset_snapshot_sync_run_id ON asset_snapshot (sync_run_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260925152920_AddAssetSnapshots') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260925152920_AddAssetSnapshots', '10.0.12');
    END IF;
END $EF$;
COMMIT;

