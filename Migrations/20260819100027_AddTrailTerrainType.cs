using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddTrailTerrainType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill: every existing trail gets TerrainType = 2 (Rugged) as a
            // placeholder. This is not a classification - it is not derived from
            // the free-text Terrain field, which describes surface material, not
            // technicality. Each trail must be reviewed and corrected by hand.
            migrationBuilder.AddColumn<int>(
                name: "TerrainType",
                table: "Trails",
                type: "integer",
                nullable: false,
                defaultValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TerrainType",
                table: "Trails");
        }
    }
}
