using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{

    public partial class AddContactNumberAndEmailToRegistration : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactNumber",
                table: "EventRegistrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "EventRegistrations",
                type: "text",
                nullable: true);
        }


        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactNumber",
                table: "EventRegistrations");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "EventRegistrations");
        }
    }
}
