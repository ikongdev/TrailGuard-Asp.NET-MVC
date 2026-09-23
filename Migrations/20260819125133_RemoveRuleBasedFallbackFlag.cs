using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{

    public partial class RemoveRuleBasedFallbackFlag : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRuleBasedFallback",
                table: "Assessments");
        }


        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRuleBasedFallback",
                table: "Assessments",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
