using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{

    public partial class RemoveEventMasl : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MASL",
                table: "Events");
        }


        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MASL",
                table: "Events",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
