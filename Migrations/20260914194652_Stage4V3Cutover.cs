using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{

    public partial class Stage4V3Cutover : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfidenceScore",
                table: "SuitabilityResults");

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
                name: "ExperienceScore",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "FitnessScore",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "GearScore",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "HealthScore",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "TotalScore",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "NpsScore",
                table: "SuitabilityResults");

            migrationBuilder.AddColumn<double>(
                name: "CompletionProbability",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "ShapValues",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DisplayFriendlyName",
                table: "ShapValues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "ShapValues",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DisplaySharePct",
                table: "ShapValues",
                type: "double precision",
                nullable: true);
        }


        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "ShapValues");

            migrationBuilder.DropColumn(
                name: "DisplayFriendlyName",
                table: "ShapValues");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "ShapValues");

            migrationBuilder.DropColumn(
                name: "DisplaySharePct",
                table: "ShapValues");

            migrationBuilder.DropColumn(
                name: "CompletionProbability",
                table: "SuitabilityResults");

            migrationBuilder.AddColumn<double>(
                name: "NpsScore",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "ConfidenceScore",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

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

            migrationBuilder.AddColumn<int>(
                name: "ExperienceScore",
                table: "Assessments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FitnessScore",
                table: "Assessments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GearScore",
                table: "Assessments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HealthScore",
                table: "Assessments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalScore",
                table: "Assessments",
                type: "integer",
                nullable: true);
        }
    }
}
