using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{

    public partial class AddPublicProfileIdToUsers : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {








            migrationBuilder.AddColumn<Guid>(
                name: "PublicProfileId",
                table: "Users",
                type: "uuid",
                nullable: true);






            migrationBuilder.Sql(
                "UPDATE \"Users\" SET \"PublicProfileId\" = gen_random_uuid() WHERE \"PublicProfileId\" IS NULL;");



            migrationBuilder.AlterColumn<Guid>(
                name: "PublicProfileId",
                table: "Users",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);


            migrationBuilder.CreateIndex(
                name: "IX_Users_PublicProfileId",
                table: "Users",
                column: "PublicProfileId",
                unique: true);
        }


        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_PublicProfileId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PublicProfileId",
                table: "Users");
        }
    }
}
