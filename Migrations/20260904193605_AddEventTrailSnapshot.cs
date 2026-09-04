using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddEventTrailSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "TrailAdjustedRatingSnapshot",
                table: "Events",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "TrailClassSnapshot",
                table: "Events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "TrailDistanceKmSnapshot",
                table: "Events",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "TrailElevationGainMetersSnapshot",
                table: "Events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TrailNameSnapshot",
                table: "Events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TrailTerrainSnapshot",
                table: "Events",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TrailThumbnailUrlSnapshot",
                table: "Events",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            // Backfill every existing Event's Trail Snapshot from its currently
            // linked Trail - see CLAUDE.md, "Event Trail Snapshot", "Existing
            // Event Migration and Backfill". This can only ever capture the
            // Trail values available NOW, at migration time; if a Trail was
            // edited since a given Event was originally created, this backfill
            // freezes the Trail's current (possibly already-edited) values, not
            // the ones that existed when the Event was created - that
            // limitation cannot be reconstructed from anywhere in this schema
            // and is documented in CLAUDE.md.
            //
            // Difficulty is also recomputed here (never left as whatever was
            // last stored) so it is guaranteed self-consistent with the newly
            // backfilled TrailAdjustedRatingSnapshot going forward - in
            // practice this reasserts the same value Event.Difficulty already
            // held for the overwhelming majority of rows, since
            // TrailController.EditTrail previously cascaded a live recompute
            // onto every linked Event on every Trail edit (a behavior this
            // same milestone removes).
            //
            // Events.TrailId is a required (non-null), FK-restricted column
            // (Trails cannot be deleted while any Event references them - see
            // ApplicationDbContext's DeleteBehavior.Restrict), so under normal
            // operation every Event row has a matching Trails row. The
            // subquery join below still safely leaves any Event whose TrailId
            // has no matching Trail row (an otherwise-impossible legacy state)
            // at the column defaults added above (empty name/terrain, zero
            // distance/elevation/class/rating, null thumbnail) rather than
            // fabricating a value - this migration does not assume such rows
            // exist, and none were found against the current development
            // database (see the implementation report for the verification
            // query and its result).
            migrationBuilder.Sql(@"
                UPDATE ""Events"" AS e
                SET
                    ""TrailNameSnapshot"" = calc.""Name"",
                    ""TrailDistanceKmSnapshot"" = calc.""DistanceKm"",
                    ""TrailElevationGainMetersSnapshot"" = calc.""ElevationGainMeters"",
                    ""TrailTerrainSnapshot"" = calc.""Terrain"",
                    ""TrailClassSnapshot"" = calc.""TrailClass"",
                    ""TrailThumbnailUrlSnapshot"" = calc.""ThumbnailUrl"",
                    ""TrailAdjustedRatingSnapshot"" = calc.""AdjustedRating"",
                    ""Difficulty"" = CASE
                        WHEN calc.""AdjustedRating"" < 81 THEN 'Easy'
                        WHEN calc.""AdjustedRating"" < 354 THEN 'Minor Climb'
                        WHEN calc.""AdjustedRating"" < 411 THEN 'Major Climb'
                        ELSE 'Major Climb — Difficult'
                    END
                FROM (
                    SELECT
                        ""Id"",
                        ""Name"",
                        ""DistanceKm"",
                        ""ElevationGainMeters"",
                        ""Terrain"",
                        ""TrailClass"",
                        ""ThumbnailUrl"",
                        sqrt(""ElevationGainMeters"" * 3.28084 * 2.0 * (""DistanceKm"" / 1.60934)) *
                            (CASE ""TrailClass""
                                WHEN 1 THEN 1.00
                                WHEN 2 THEN 1.15
                                WHEN 3 THEN 1.35
                                WHEN 4 THEN 1.60
                                ELSE 1.00
                             END) AS ""AdjustedRating""
                    FROM ""Trails""
                ) AS calc
                WHERE e.""TrailId"" = calc.""Id"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrailAdjustedRatingSnapshot",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "TrailClassSnapshot",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "TrailDistanceKmSnapshot",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "TrailElevationGainMetersSnapshot",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "TrailNameSnapshot",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "TrailTerrainSnapshot",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "TrailThumbnailUrlSnapshot",
                table: "Events");
        }
    }
}
