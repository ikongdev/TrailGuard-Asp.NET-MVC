using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class UpdateTrailWithImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ImageUrl",
                table: "Trails",
                newName: "ThumbnailUrl");

            migrationBuilder.AddColumn<string>(
                name: "AdditionalMediaUrls",
                table: "Trails",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Trails",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Duration",
                table: "Trails",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdditionalMediaUrls",
                table: "Trails");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Trails");

            migrationBuilder.DropColumn(
                name: "Duration",
                table: "Trails");

            migrationBuilder.RenameColumn(
                name: "ThumbnailUrl",
                table: "Trails",
                newName: "ImageUrl");
        }
    }
}
