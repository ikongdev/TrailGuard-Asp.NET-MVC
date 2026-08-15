using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GroupManagement",
                table: "EventFeedbacks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HazardsEncountered",
                table: "EventFeedbacks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreEventCommunication",
                table: "EventFeedbacks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafetyManagement",
                table: "EventFeedbacks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrailCondition",
                table: "EventFeedbacks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrailSignage",
                table: "EventFeedbacks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaterSourceAvailability",
                table: "EventFeedbacks",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GroupManagement",
                table: "EventFeedbacks");

            migrationBuilder.DropColumn(
                name: "HazardsEncountered",
                table: "EventFeedbacks");

            migrationBuilder.DropColumn(
                name: "PreEventCommunication",
                table: "EventFeedbacks");

            migrationBuilder.DropColumn(
                name: "SafetyManagement",
                table: "EventFeedbacks");

            migrationBuilder.DropColumn(
                name: "TrailCondition",
                table: "EventFeedbacks");

            migrationBuilder.DropColumn(
                name: "TrailSignage",
                table: "EventFeedbacks");

            migrationBuilder.DropColumn(
                name: "WaterSourceAvailability",
                table: "EventFeedbacks");
        }
    }
}
