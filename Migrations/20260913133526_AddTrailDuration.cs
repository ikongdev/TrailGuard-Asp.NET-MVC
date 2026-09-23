using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{

    public partial class AddTrailDuration : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TypicalDurationHours",
                table: "Trails",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TrailDurationHoursSnapshot",
                table: "Events",
                type: "numeric",
                nullable: true);





            migrationBuilder.Sql("""
                UPDATE "Trails" AS t
                SET "TypicalDurationHours" = reference.duration
                FROM (VALUES
                    ('Mt. Pulag (Ambangeg)', 5.47),
                    ('Mt. Pulag', 5.47),
                    ('Mt. Pinatubo (crater)', 4.17),
                    ('Mt. Pinatubo', 4.17),
                    ('Mt. Tapulao (Dampay)', 12.25),
                    ('Mt. Tapulao', 12.25),
                    ('Mt. Talamitam', 2.90),
                    ('Mt. Makiling (UPLB)', 6.45),
                    ('Mt. Makiling', 6.45),
                    ('Mt. Ayaas (Mascap)', 4.05),
                    ('Mt. Ayaas', 4.05),
                    ('Mt. Pamitinan (Wawa)', 1.62),
                    ('Mt. Pamitinan', 1.62),
                    ('Mt. Hapunang Banoi (Wawa)', 2.15),
                    ('Mt. Hapunang Banoi', 2.15),
                    ('Mt. Manalmon (Madlum)', 1.17),
                    ('Mt. Manalmon', 1.17),
                    ('Mt. Kitanglad (Intavas)', 8.27),
                    ('Mt. Kitanglad', 8.27),
                    ('Mt. Dulang-Dulang (Bol-ogan)', 8.23),
                    ('Mt. Dulang-Dulang', 8.23),
                    ('Mt. Tagapo (Janosa)', 2.37),
                    ('Mt. Tagapo', 2.37)
                ) AS reference(name, duration)
                WHERE t."Name" = reference.name;

                DO $duration_backfill$
                DECLARE unmatched text;
                BEGIN
                    SELECT string_agg(format('%s (%s)', "Name", "Id"), ', ' ORDER BY "Id")
                    INTO unmatched FROM "Trails" WHERE "TypicalDurationHours" IS NULL;
                    IF unmatched IS NOT NULL THEN
                        RAISE EXCEPTION 'Trail duration backfill requires recorded values or the user-run development catalog cleanup. Unmatched trails: %', unmatched;
                    END IF;
                END $duration_backfill$;

                UPDATE "Events" AS e
                SET "TrailDurationHoursSnapshot" = t."TypicalDurationHours"
                FROM "Trails" AS t
                WHERE e."TrailId" = t."Id";

                DO $duration_snapshot$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Events" WHERE "TrailDurationHoursSnapshot" IS NULL) THEN
                        RAISE EXCEPTION 'Event duration backfill failed: an Event has no linked Trail with a recorded duration.';
                    END IF;
                END $duration_snapshot$;
                """);

            migrationBuilder.AlterColumn<decimal>(
                name: "TypicalDurationHours",
                table: "Trails",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "TrailDurationHoursSnapshot",
                table: "Events",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Trails_TypicalDurationHours_Positive",
                table: "Trails",
                sql: "\"TypicalDurationHours\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Events_TrailDurationHoursSnapshot_Positive",
                table: "Events",
                sql: "\"TrailDurationHoursSnapshot\" > 0");
        }


        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Trails_TypicalDurationHours_Positive",
                table: "Trails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Events_TrailDurationHoursSnapshot_Positive",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "TypicalDurationHours",
                table: "Trails");

            migrationBuilder.DropColumn(
                name: "TrailDurationHoursSnapshot",
                table: "Events");
        }
    }
}
