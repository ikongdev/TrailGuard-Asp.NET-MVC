using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{

    public partial class AddEventTrailSnapshot : Migration
    {

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
