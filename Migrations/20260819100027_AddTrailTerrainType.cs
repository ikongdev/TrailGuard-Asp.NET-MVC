using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{

    public partial class AddTrailTerrainType : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {




            migrationBuilder.AddColumn<int>(
                name: "TerrainType",
                table: "Trails",
                type: "integer",
                nullable: false,
                defaultValue: 2);
        }


        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TerrainType",
                table: "Trails");
        }
    }
}
