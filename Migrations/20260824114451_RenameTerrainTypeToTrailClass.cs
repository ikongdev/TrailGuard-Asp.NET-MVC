using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class RenameTerrainTypeToTrailClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TerrainType",
                table: "Trails",
                newName: "TrailClass");

            // Existing values (1/2/3) map directly onto the new 4-class PinoyMountaineer
            // Trail Class scale (1 Walking / 2 Hiking / 3 Scrambling / 4 Simple Climbing)
            // - no data conversion needed. But a trail rated 3 under the old 3-level
            // scheme may now warrant a 4 (e.g. Mt. Guiting-Guiting-type trails with fixed
            // ropes and real exposure), so print every trail for manual review rather than
            // silently trusting the carry-over.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    r RECORD;
                BEGIN
                    FOR r IN SELECT ""Id"", ""Name"", ""TrailClass"" FROM ""Trails"" ORDER BY ""Id"" LOOP
                        RAISE NOTICE 'Trail % (Id=%): TrailClass=%  -- review for possible Class 4 (fixed ropes / exposure)', r.""Name"", r.""Id"", r.""TrailClass"";
                    END LOOP;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TrailClass",
                table: "Trails",
                newName: "TerrainType");
        }
    }
}
