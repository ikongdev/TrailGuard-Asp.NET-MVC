using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteEffortDifficulty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TrailAdjustedRatingSnapshot",
                table: "Events",
                newName: "DifficultyScoreSnapshot");

            migrationBuilder.AlterColumn<decimal>(
                name: "DifficultyScoreSnapshot",
                table: "Events",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "double precision");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "DifficultyScoreSnapshot",
                table: "Events",
                type: "double precision",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.RenameColumn(
                name: "DifficultyScoreSnapshot",
                table: "Events",
                newName: "TrailAdjustedRatingSnapshot");
        }
    }
}
