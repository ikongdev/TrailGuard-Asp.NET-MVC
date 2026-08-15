using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddEventCompletionAndFinalLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Events",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "Events",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletedBy",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FinalSuitabilityLabels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RegistrationId = table.Column<int>(type: "integer", nullable: false),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    AssessmentId = table.Column<int>(type: "integer", nullable: false),
                    PreHikeLabel = table.Column<string>(type: "text", nullable: false),
                    ParticipantFeedback = table.Column<string>(type: "text", nullable: true),
                    OrganizerAssessment = table.Column<string>(type: "text", nullable: true),
                    FinalLabel = table.Column<string>(type: "text", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinalSuitabilityLabels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinalSuitabilityLabels_Assessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "Assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FinalSuitabilityLabels_EventRegistrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalTable: "EventRegistrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FinalSuitabilityLabels_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FinalSuitabilityLabels_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinalSuitabilityLabels_AssessmentId",
                table: "FinalSuitabilityLabels",
                column: "AssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FinalSuitabilityLabels_EventId",
                table: "FinalSuitabilityLabels",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_FinalSuitabilityLabels_RegistrationId",
                table: "FinalSuitabilityLabels",
                column: "RegistrationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinalSuitabilityLabels_UserId",
                table: "FinalSuitabilityLabels",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinalSuitabilityLabels");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "CompletedBy",
                table: "Events");
        }
    }
}
