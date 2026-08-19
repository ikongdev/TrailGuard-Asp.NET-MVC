using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddGateResultFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GateApplied",
                table: "SuitabilityResults",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GateReason",
                table: "SuitabilityResults",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ModelLabel",
                table: "SuitabilityResults",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NpsBand",
                table: "SuitabilityResults",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "NpsScore",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<bool>(
                name: "IsRuleBasedFallback",
                table: "Assessments",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GateApplied",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "GateReason",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "ModelLabel",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "NpsBand",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "NpsScore",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "IsRuleBasedFallback",
                table: "Assessments");
        }
    }
}
