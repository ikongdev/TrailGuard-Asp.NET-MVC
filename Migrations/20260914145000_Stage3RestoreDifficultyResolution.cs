using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{

    public partial class Stage3RestoreDifficultyResolution : Migration
    {

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
