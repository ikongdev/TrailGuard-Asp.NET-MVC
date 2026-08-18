using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddAcsmScreeningFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Gender",
                table: "Assessments");

            migrationBuilder.AddColumn<string>(
                name: "ExerciseConsistency",
                table: "Assessments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MedicalClearanceRequired",
                table: "Assessments",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MedicalClearanceRequired",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "ExerciseConsistency",
                table: "Assessments");

            migrationBuilder.AddColumn<string>(
                name: "Gender",
                table: "Assessments",
                type: "text",
                nullable: true);
        }
    }
}
