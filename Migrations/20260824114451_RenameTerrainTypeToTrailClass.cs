using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{

    public partial class RenameTerrainTypeToTrailClass : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TerrainType",
                table: "Trails",
                newName: "TrailClass");







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


        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TrailClass",
                table: "Trails",
                newName: "TerrainType");
        }
    }
}
