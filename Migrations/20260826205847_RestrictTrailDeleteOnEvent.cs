using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{

    public partial class RestrictTrailDeleteOnEvent : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_Trails_TrailId",
                table: "Events");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Trails_TrailId",
                table: "Events",
                column: "TrailId",
                principalTable: "Trails",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }


        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_Trails_TrailId",
                table: "Events");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Trails_TrailId",
                table: "Events",
                column: "TrailId",
                principalTable: "Trails",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
