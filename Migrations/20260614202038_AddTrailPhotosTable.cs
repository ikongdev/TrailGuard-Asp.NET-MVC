using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddTrailPhotosTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // migrationBuilder.DropColumn(
            //     name: "AdditionalMediaUrls",
            //     table: "Trails");

            // migrationBuilder.DropColumn(
            //     name: "Difficulty",
            //     table: "Trails");

            // migrationBuilder.DropColumn(
            //     name: "Duration",
            //     table: "Trails");

            migrationBuilder.CreateTable(
                name: "TrailPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TrailId = table.Column<int>(type: "int", nullable: false),
                    ImageUrl = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Caption = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrailPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrailPhotos_Trails_TrailId",
                        column: x => x.TrailId,
                        principalTable: "Trails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TrailPhotos_TrailId",
                table: "TrailPhotos",
                column: "TrailId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrailPhotos");

            // migrationBuilder.AddColumn<string>(
            //     name: "AdditionalMediaUrls",
            //     table: "Trails",
            //     type: "longtext",
            //     nullable: true)
            //     .Annotation("MySql:CharSet", "utf8mb4");

            // migrationBuilder.AddColumn<string>(
            //     name: "Difficulty",
            //     table: "Trails",
            //     type: "longtext",
            //     nullable: false)
            //     .Annotation("MySql:CharSet", "utf8mb4");

            // migrationBuilder.AddColumn<string>(
            //     name: "Duration",
            //     table: "Trails",
            //     type: "longtext",
            //     nullable: false)
            //     .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
