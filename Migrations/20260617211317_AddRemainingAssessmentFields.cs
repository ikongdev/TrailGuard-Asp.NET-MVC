using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddRemainingAssessmentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assessments_Trails_TrailId",
                table: "Assessments");

            migrationBuilder.DropIndex(
                name: "IX_Assessments_TrailId",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "BMI",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "EmergencyReadiness",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "EnduranceLevel",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "FamiliarTerrain",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "FitnessLevel",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "HealthConditions",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "HikingExperience",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "MedicalNotes",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "PreviousClimbs",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "SafetyKnowledge",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "SuitabilityResult",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "SuitabilityScore",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "TrailId",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "Weight",
                table: "Assessments");

            migrationBuilder.RenameColumn(
                name: "Recommendations",
                table: "Assessments",
                newName: "TrailDifficultyCompleted");

            migrationBuilder.RenameColumn(
                name: "DetailedFeedback",
                table: "Assessments",
                newName: "Result");

            migrationBuilder.RenameColumn(
                name: "DateTaken",
                table: "Assessments",
                newName: "SubmittedAt");

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactName",
                table: "EventRegistrations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactNumber",
                table: "EventRegistrations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PaymentReceiptUrl",
                table: "EventRegistrations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<int>(
                name: "GearScore",
                table: "Assessments",
                type: "int",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double");

            migrationBuilder.AlterColumn<string>(
                name: "GearItems",
                table: "Assessments",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "ExerciseFrequency",
                table: "Assessments",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<int>(
                name: "Age",
                table: "Assessments",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "CardioEndurance",
                table: "Assessments",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "ConsentGiven",
                table: "Assessments",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ExerciseType",
                table: "Assessments",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "ExperienceScore",
                table: "Assessments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FitnessScore",
                table: "Assessments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gender",
                table: "Assessments",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "HealthScore",
                table: "Assessments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HeightCm",
                table: "Assessments",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MedicalConditions",
                table: "Assessments",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "MountainsClimbed",
                table: "Assessments",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RecencyOfHike",
                table: "Assessments",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "TotalScore",
                table: "Assessments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "WeightKg",
                table: "Assessments",
                type: "double",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmergencyContactName",
                table: "EventRegistrations");

            migrationBuilder.DropColumn(
                name: "EmergencyContactNumber",
                table: "EventRegistrations");

            migrationBuilder.DropColumn(
                name: "PaymentReceiptUrl",
                table: "EventRegistrations");

            migrationBuilder.DropColumn(
                name: "CardioEndurance",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "ConsentGiven",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "ExerciseType",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "ExperienceScore",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "FitnessScore",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "HealthScore",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "HeightCm",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "MedicalConditions",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "MountainsClimbed",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "RecencyOfHike",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "TotalScore",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "WeightKg",
                table: "Assessments");

            migrationBuilder.RenameColumn(
                name: "TrailDifficultyCompleted",
                table: "Assessments",
                newName: "Recommendations");

            migrationBuilder.RenameColumn(
                name: "SubmittedAt",
                table: "Assessments",
                newName: "DateTaken");

            migrationBuilder.RenameColumn(
                name: "Result",
                table: "Assessments",
                newName: "DetailedFeedback");

            migrationBuilder.AlterColumn<double>(
                name: "GearScore",
                table: "Assessments",
                type: "double",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "Assessments",
                keyColumn: "GearItems",
                keyValue: null,
                column: "GearItems",
                value: "");

            migrationBuilder.AlterColumn<string>(
                name: "GearItems",
                table: "Assessments",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "Assessments",
                keyColumn: "ExerciseFrequency",
                keyValue: null,
                column: "ExerciseFrequency",
                value: "");

            migrationBuilder.AlterColumn<string>(
                name: "ExerciseFrequency",
                table: "Assessments",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<int>(
                name: "Age",
                table: "Assessments",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BMI",
                table: "Assessments",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyReadiness",
                table: "Assessments",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "EnduranceLevel",
                table: "Assessments",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "FamiliarTerrain",
                table: "Assessments",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "FitnessLevel",
                table: "Assessments",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "HealthConditions",
                table: "Assessments",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "Height",
                table: "Assessments",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "HikingExperience",
                table: "Assessments",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "MedicalNotes",
                table: "Assessments",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "PreviousClimbs",
                table: "Assessments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SafetyKnowledge",
                table: "Assessments",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SuitabilityResult",
                table: "Assessments",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "SuitabilityScore",
                table: "Assessments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TrailId",
                table: "Assessments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "Weight",
                table: "Assessments",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_TrailId",
                table: "Assessments",
                column: "TrailId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assessments_Trails_TrailId",
                table: "Assessments",
                column: "TrailId",
                principalTable: "Trails",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
