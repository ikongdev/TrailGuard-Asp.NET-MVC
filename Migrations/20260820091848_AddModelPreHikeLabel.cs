using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{

    public partial class AddModelPreHikeLabel : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModelPreHikeLabel",
                table: "FinalSuitabilityLabels",
                type: "text",
                nullable: true);
        }


        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModelPreHikeLabel",
                table: "FinalSuitabilityLabels");
        }
    }
}
