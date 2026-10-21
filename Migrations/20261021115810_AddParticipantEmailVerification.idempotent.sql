START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021115810_AddParticipantEmailVerification') THEN
    ALTER TABLE "Users" ADD "ParticipantEmailVerificationRequired" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021115810_AddParticipantEmailVerification') THEN
    ALTER TABLE "Users" ADD "LastEmailVerificationSentAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021115810_AddParticipantEmailVerification') THEN
    ALTER TABLE "Users" ADD "PendingEmailChange" character varying(256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021115810_AddParticipantEmailVerification') THEN
    ALTER TABLE "Users" ADD "PendingEmailChangeRequestedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021115810_AddParticipantEmailVerification') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261021115810_AddParticipantEmailVerification', '10.0.10');
    END IF;
END $EF$;
COMMIT;

