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

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926080544_AddGamificationPoints') THEN
    CREATE TABLE point_transaction (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        submission_id uuid,
        amount integer NOT NULL,
        reason character varying(24) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_point_transaction PRIMARY KEY (id),
        CONSTRAINT ck_point_transaction_amount CHECK (amount <> 0),
        CONSTRAINT fk_point_transaction_submission_submission_id FOREIGN KEY (submission_id) REFERENCES submission (id),
        CONSTRAINT fk_point_transaction_user_user_id FOREIGN KEY (user_id) REFERENCES "user" (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926080544_AddGamificationPoints') THEN
    CREATE UNIQUE INDEX ix_point_transaction_submission_id_reason ON point_transaction (submission_id, reason) WHERE submission_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926080544_AddGamificationPoints') THEN
    CREATE INDEX ix_point_transaction_user_id_created_at ON point_transaction (user_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926080544_AddGamificationPoints') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260926080544_AddGamificationPoints', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926085336_AddCitiesAndDistricts') THEN
    ALTER TABLE point_transaction ADD district_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926085336_AddCitiesAndDistricts') THEN
    CREATE TABLE city (
        id uuid NOT NULL,
        key character varying(64) NOT NULL,
        name character varying(128) NOT NULL,
        country_code character varying(2),
        center_lat double precision,
        center_lon double precision,
        default_zoom integer,
        timezone character varying(64) NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_city PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926085336_AddCitiesAndDistricts') THEN
    CREATE TABLE district (
        id uuid NOT NULL,
        city_id uuid NOT NULL,
        key character varying(64) NOT NULL,
        name character varying(128) NOT NULL,
        description character varying(2000),
        color character varying(7),
        is_active boolean NOT NULL,
        total_points integer NOT NULL,
        geom geography (polygon) NOT NULL,
        centroid_lat double precision NOT NULL,
        centroid_lon double precision NOT NULL,
        created_by uuid,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_district PRIMARY KEY (id),
        CONSTRAINT fk_district_city_city_id FOREIGN KEY (city_id) REFERENCES city (id) ON DELETE RESTRICT,
        CONSTRAINT fk_district_user_created_by FOREIGN KEY (created_by) REFERENCES "user" (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926085336_AddCitiesAndDistricts') THEN
    CREATE TABLE district_point (
        id uuid NOT NULL,
        district_id uuid NOT NULL,
        position integer NOT NULL,
        lat double precision NOT NULL,
        lon double precision NOT NULL,
        CONSTRAINT pk_district_point PRIMARY KEY (id),
        CONSTRAINT ck_district_point_position CHECK (position >= 0),
        CONSTRAINT fk_district_point_district_district_id FOREIGN KEY (district_id) REFERENCES district (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926085336_AddCitiesAndDistricts') THEN
    CREATE INDEX ix_point_transaction_district_id_created_at ON point_transaction (district_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926085336_AddCitiesAndDistricts') THEN
    CREATE UNIQUE INDEX ix_city_key ON city (key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926085336_AddCitiesAndDistricts') THEN
    CREATE UNIQUE INDEX ix_district_city_id_key ON district (city_id, key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926085336_AddCitiesAndDistricts') THEN
    CREATE UNIQUE INDEX ix_district_city_id_name ON district (city_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926085336_AddCitiesAndDistricts') THEN
    CREATE INDEX ix_district_created_by ON district (created_by);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926085336_AddCitiesAndDistricts') THEN
    CREATE INDEX ix_district_geom ON district USING gist (geom);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926085336_AddCitiesAndDistricts') THEN
    CREATE UNIQUE INDEX ix_district_point_district_id_position ON district_point (district_id, position);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926085336_AddCitiesAndDistricts') THEN
    ALTER TABLE point_transaction ADD CONSTRAINT fk_point_transaction_district_district_id FOREIGN KEY (district_id) REFERENCES district (id) ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926085336_AddCitiesAndDistricts') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260926085336_AddCitiesAndDistricts', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926094746_AddCards') THEN
    ALTER TABLE district ADD genus_stats_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926094746_AddCards') THEN
    ALTER TABLE district ADD known_genus_trees integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926094746_AddCards') THEN
    CREATE TABLE card (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        submission_id uuid NOT NULL,
        asset_id uuid NOT NULL,
        district_id uuid,
        genus character varying(128) NOT NULL,
        rarity character varying(24) NOT NULL,
        frequency character varying(24) NOT NULL,
        share double precision,
        reasons jsonb NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_card PRIMARY KEY (id),
        CONSTRAINT fk_card_asset_asset_id FOREIGN KEY (asset_id) REFERENCES asset (id) ON DELETE CASCADE,
        CONSTRAINT fk_card_district_district_id FOREIGN KEY (district_id) REFERENCES district (id) ON DELETE SET NULL,
        CONSTRAINT fk_card_submission_submission_id FOREIGN KEY (submission_id) REFERENCES submission (id) ON DELETE CASCADE,
        CONSTRAINT fk_card_user_user_id FOREIGN KEY (user_id) REFERENCES "user" (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926094746_AddCards') THEN
    CREATE TABLE district_genus_stat (
        district_id uuid NOT NULL,
        genus character varying(128) NOT NULL,
        tree_count integer NOT NULL,
        CONSTRAINT pk_district_genus_stat PRIMARY KEY (district_id, genus),
        CONSTRAINT fk_district_genus_stat_district_district_id FOREIGN KEY (district_id) REFERENCES district (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926094746_AddCards') THEN
    CREATE INDEX ix_card_asset_id ON card (asset_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926094746_AddCards') THEN
    CREATE INDEX ix_card_district_id ON card (district_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926094746_AddCards') THEN
    CREATE UNIQUE INDEX ix_card_submission_id ON card (submission_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926094746_AddCards') THEN
    CREATE INDEX ix_card_user_id_created_at ON card (user_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926094746_AddCards') THEN
    CREATE INDEX ix_card_user_id_genus ON card (user_id, genus);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926094746_AddCards') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260926094746_AddCards', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926101009_AddReportsAndReadings') THEN
    CREATE TABLE asset_report (
        id uuid NOT NULL,
        data_source_id uuid NOT NULL,
        external_id character varying(128) NOT NULL,
        category character varying(48) NOT NULL,
        status character varying(24) NOT NULL,
        description text,
        status_notes text,
        address text,
        media_url text,
        geom geography (point) NOT NULL,
        asset_id uuid,
        distance_m double precision,
        reported_at timestamp with time zone NOT NULL,
        source_updated_at timestamp with time zone,
        raw jsonb NOT NULL,
        source_hash character varying(64) NOT NULL,
        first_seen_at timestamp with time zone NOT NULL,
        last_seen_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_asset_report PRIMARY KEY (id),
        CONSTRAINT fk_asset_report_assets_asset_id FOREIGN KEY (asset_id) REFERENCES asset (id) ON DELETE SET NULL,
        CONSTRAINT fk_asset_report_data_source_data_source_id FOREIGN KEY (data_source_id) REFERENCES data_source (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926101009_AddReportsAndReadings') THEN
    CREATE TABLE environment_reading (
        id uuid NOT NULL,
        data_source_id uuid NOT NULL,
        station_id character varying(64) NOT NULL,
        metric character varying(64) NOT NULL,
        value double precision NOT NULL,
        unit character varying(16) NOT NULL,
        measured_at timestamp with time zone NOT NULL,
        geom geography (point),
        imported_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_environment_reading PRIMARY KEY (id),
        CONSTRAINT fk_environment_reading_data_source_data_source_id FOREIGN KEY (data_source_id) REFERENCES data_source (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926101009_AddReportsAndReadings') THEN
    CREATE INDEX ix_asset_report_asset_id_status ON asset_report (asset_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926101009_AddReportsAndReadings') THEN
    CREATE INDEX ix_asset_report_category_status_reported_at ON asset_report (category, status, reported_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926101009_AddReportsAndReadings') THEN
    CREATE UNIQUE INDEX ix_asset_report_data_source_id_external_id ON asset_report (data_source_id, external_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926101009_AddReportsAndReadings') THEN
    CREATE INDEX ix_asset_report_geom ON asset_report USING gist (geom);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926101009_AddReportsAndReadings') THEN
    CREATE UNIQUE INDEX ix_environment_reading_data_source_id_station_id_metric_measur ON environment_reading (data_source_id, station_id, metric, measured_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926101009_AddReportsAndReadings') THEN
    CREATE INDEX ix_environment_reading_metric_measured_at ON environment_reading (metric, measured_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926101009_AddReportsAndReadings') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260926101009_AddReportsAndReadings', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926103512_AddStaleQuests') THEN
    CREATE TABLE asset_activity (
        asset_id uuid NOT NULL,
        last_verified_at timestamp with time zone NOT NULL,
        verification_count integer NOT NULL,
        CONSTRAINT pk_asset_activity PRIMARY KEY (asset_id),
        CONSTRAINT ck_asset_activity_count CHECK (verification_count >= 1),
        CONSTRAINT fk_asset_activity_asset_asset_id FOREIGN KEY (asset_id) REFERENCES asset (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926103512_AddStaleQuests') THEN
    CREATE TABLE quest_schedule (
        id uuid NOT NULL,
        name character varying(128) NOT NULL,
        city_id uuid NOT NULL,
        is_enabled boolean NOT NULL,
        weekday integer NOT NULL,
        time_of_day time without time zone NOT NULL,
        duration_hours integer NOT NULL,
        task_type character varying(48) NOT NULL,
        title character varying(200),
        description text,
        task_config jsonb NOT NULL,
        target jsonb NOT NULL,
        max_completions integer NOT NULL,
        reward_points integer NOT NULL,
        geofence_radius_m integer,
        claim_ttl_minutes integer,
        created_by uuid NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_quest_schedule PRIMARY KEY (id),
        CONSTRAINT ck_quest_schedule_duration CHECK (duration_hours BETWEEN 1 AND 168),
        CONSTRAINT ck_quest_schedule_weekday CHECK (weekday BETWEEN 0 AND 6),
        CONSTRAINT fk_quest_schedule_city_city_id FOREIGN KEY (city_id) REFERENCES city (id) ON DELETE RESTRICT,
        CONSTRAINT fk_quest_schedule_user_created_by FOREIGN KEY (created_by) REFERENCES "user" (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926103512_AddStaleQuests') THEN
    CREATE TABLE quest_schedule_run (
        schedule_id uuid NOT NULL,
        period_key character varying(48) NOT NULL,
        ran_at timestamp with time zone NOT NULL,
        campaign_id uuid,
        quests_created integer NOT NULL,
        error text,
        CONSTRAINT pk_quest_schedule_run PRIMARY KEY (schedule_id, period_key),
        CONSTRAINT fk_quest_schedule_run_quest_campaign_campaign_id FOREIGN KEY (campaign_id) REFERENCES quest_campaign (id) ON DELETE SET NULL,
        CONSTRAINT fk_quest_schedule_run_quest_schedule_schedule_id FOREIGN KEY (schedule_id) REFERENCES quest_schedule (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926103512_AddStaleQuests') THEN
    CREATE INDEX ix_asset_activity_last_verified_at ON asset_activity (last_verified_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926103512_AddStaleQuests') THEN
    CREATE INDEX ix_quest_schedule_city_id ON quest_schedule (city_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926103512_AddStaleQuests') THEN
    CREATE INDEX ix_quest_schedule_created_by ON quest_schedule (created_by);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926103512_AddStaleQuests') THEN
    CREATE UNIQUE INDEX ix_quest_schedule_name ON quest_schedule (name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926103512_AddStaleQuests') THEN
    CREATE INDEX ix_quest_schedule_run_campaign_id ON quest_schedule_run (campaign_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926103512_AddStaleQuests') THEN
    CREATE INDEX ix_quest_schedule_run_schedule_id_ran_at ON quest_schedule_run (schedule_id, ran_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926103512_AddStaleQuests') THEN

                    INSERT INTO asset_activity (asset_id, last_verified_at, verification_count)
                    SELECT q.asset_id, max(s.reviewed_at), count(*)
                    FROM submission s
                    JOIN claim c ON c.id = s.claim_id
                    JOIN quest q ON q.id = c.quest_id
                    WHERE s.status = 'approved' AND s.reviewed_at IS NOT NULL
                    GROUP BY q.asset_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926103512_AddStaleQuests') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260926103512_AddStaleQuests', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    ALTER TABLE quest DROP CONSTRAINT fk_quest_asset_asset_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    ALTER TABLE submission ADD auto_review jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    ALTER TABLE submission ADD auto_reviewed_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    ALTER TABLE quest ALTER COLUMN asset_id DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    ALTER TABLE quest ADD district_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    CREATE TABLE asset_proposal (
        id uuid NOT NULL,
        submission_id uuid NOT NULL,
        data_source_id uuid NOT NULL,
        asset_type_id uuid NOT NULL,
        district_id uuid,
        geom geography (point) NOT NULL,
        genus character varying(100),
        species character varying(100),
        note text,
        photo_url text,
        status character varying(24) NOT NULL,
        export_run_id uuid,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_asset_proposal PRIMARY KEY (id),
        CONSTRAINT fk_asset_proposal_asset_type_asset_type_id FOREIGN KEY (asset_type_id) REFERENCES asset_type (id) ON DELETE CASCADE,
        CONSTRAINT fk_asset_proposal_data_source_data_source_id FOREIGN KEY (data_source_id) REFERENCES data_source (id) ON DELETE CASCADE,
        CONSTRAINT fk_asset_proposal_district_district_id FOREIGN KEY (district_id) REFERENCES district (id) ON DELETE SET NULL,
        CONSTRAINT fk_asset_proposal_export_runs_export_run_id FOREIGN KEY (export_run_id) REFERENCES export_run (id),
        CONSTRAINT fk_asset_proposal_submission_submission_id FOREIGN KEY (submission_id) REFERENCES submission (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    CREATE INDEX ix_quest_district_id ON quest (district_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    ALTER TABLE quest ADD CONSTRAINT ck_quest_place CHECK (asset_id IS NOT NULL OR district_id IS NOT NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    CREATE INDEX ix_asset_proposal_asset_type_id ON asset_proposal (asset_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    CREATE INDEX ix_asset_proposal_data_source_id ON asset_proposal (data_source_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    CREATE INDEX ix_asset_proposal_district_id ON asset_proposal (district_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    CREATE INDEX ix_asset_proposal_export_run_id ON asset_proposal (export_run_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    CREATE INDEX ix_asset_proposal_geom ON asset_proposal USING gist (geom);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    CREATE INDEX ix_asset_proposal_status ON asset_proposal (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    CREATE UNIQUE INDEX ix_asset_proposal_submission_id ON asset_proposal (submission_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    ALTER TABLE quest ADD CONSTRAINT fk_quest_asset_asset_id FOREIGN KEY (asset_id) REFERENCES asset (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    ALTER TABLE quest ADD CONSTRAINT fk_quest_districts_district_id FOREIGN KEY (district_id) REFERENCES district (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926105941_AddNewTreeReportsAndAutoReview') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260926105941_AddNewTreeReportsAndAutoReview', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926122102_AddBadgesAndSyncRequests') THEN
    CREATE TABLE badge (
        id uuid NOT NULL,
        key character varying(64) NOT NULL,
        name character varying(200) NOT NULL,
        description character varying(500) NOT NULL,
        icon character varying(64) NOT NULL,
        criteria jsonb NOT NULL,
        reward_points integer NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_badge PRIMARY KEY (id),
        CONSTRAINT ck_badge_reward CHECK (reward_points >= 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926122102_AddBadgesAndSyncRequests') THEN
    CREATE TABLE sync_request (
        id uuid NOT NULL,
        data_source_key character varying(64) NOT NULL,
        force boolean NOT NULL,
        accept_schema_change boolean NOT NULL,
        requested_by uuid NOT NULL,
        requested_at timestamp with time zone NOT NULL,
        started_at timestamp with time zone,
        finished_at timestamp with time zone,
        status character varying(24) NOT NULL,
        sync_run_id uuid,
        error character varying(2000),
        CONSTRAINT pk_sync_request PRIMARY KEY (id),
        CONSTRAINT ck_sync_request_status CHECK (status IN ('pending','running','succeeded','failed')),
        CONSTRAINT fk_sync_request_sync_run_sync_run_id FOREIGN KEY (sync_run_id) REFERENCES sync_run (id) ON DELETE SET NULL,
        CONSTRAINT fk_sync_request_user_requested_by FOREIGN KEY (requested_by) REFERENCES "user" (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926122102_AddBadgesAndSyncRequests') THEN
    CREATE TABLE user_badge (
        user_id uuid NOT NULL,
        badge_id uuid NOT NULL,
        awarded_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_user_badge PRIMARY KEY (user_id, badge_id),
        CONSTRAINT fk_user_badge_badge_badge_id FOREIGN KEY (badge_id) REFERENCES badge (id) ON DELETE CASCADE,
        CONSTRAINT fk_user_badge_user_user_id FOREIGN KEY (user_id) REFERENCES "user" (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926122102_AddBadgesAndSyncRequests') THEN
    CREATE UNIQUE INDEX ix_badge_key ON badge (key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926122102_AddBadgesAndSyncRequests') THEN
    CREATE UNIQUE INDEX ix_sync_request_data_source_key ON sync_request (data_source_key) WHERE status IN ('pending','running');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926122102_AddBadgesAndSyncRequests') THEN
    CREATE INDEX ix_sync_request_requested_by ON sync_request (requested_by);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926122102_AddBadgesAndSyncRequests') THEN
    CREATE INDEX ix_sync_request_status_requested_at ON sync_request (status, requested_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926122102_AddBadgesAndSyncRequests') THEN
    CREATE INDEX ix_sync_request_sync_run_id ON sync_request (sync_run_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926122102_AddBadgesAndSyncRequests') THEN
    CREATE INDEX ix_user_badge_badge_id ON user_badge (badge_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926122102_AddBadgesAndSyncRequests') THEN
    CREATE INDEX ix_user_badge_user_id_awarded_at ON user_badge (user_id, awarded_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926122102_AddBadgesAndSyncRequests') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260926122102_AddBadgesAndSyncRequests', '10.0.12');
    END IF;
END $EF$;
COMMIT;

