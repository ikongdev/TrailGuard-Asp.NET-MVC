using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{

    public partial class RenameAnnouncementsAndAddWeatherFields : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Announcements",
                table: "Events",
                newName: "NotesAndReminders");

            migrationBuilder.AddColumn<string>(
                name: "WeatherRiskLevel",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeatherReminder",
                table: "Events",
                type: "text",
                nullable: true);
        }


        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WeatherRiskLevel",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "WeatherReminder",
                table: "Events");

            migrationBuilder.RenameColumn(
                name: "NotesAndReminders",
                table: "Events",
                newName: "Announcements");
        }
    }
}
