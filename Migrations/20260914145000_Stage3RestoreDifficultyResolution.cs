using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class Stage3RestoreDifficultyResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrganizerAssessment",
                table: "FinalSuitabilityLabels",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParticipantFeedback",
                table: "FinalSuitabilityLabels",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrganizerAssessment",
                table: "FinalSuitabilityLabels");

            migrationBuilder.DropColumn(
                name: "ParticipantFeedback",
                table: "FinalSuitabilityLabels");
        }
    }
}
