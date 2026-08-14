using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationWorkflowFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "EventRegistrations",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionReason",
                table: "EventRegistrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MedicalClearanceUrl",
                table: "EventRegistrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentDeadline",
                table: "EventRegistrations",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentReceiptUploadedAt",
                table: "EventRegistrations",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreparationPlan",
                table: "EventRegistrations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "EventRegistrations");

            migrationBuilder.DropColumn(
                name: "DecisionReason",
                table: "EventRegistrations");

            migrationBuilder.DropColumn(
                name: "MedicalClearanceUrl",
                table: "EventRegistrations");

            migrationBuilder.DropColumn(
                name: "PaymentDeadline",
                table: "EventRegistrations");

            migrationBuilder.DropColumn(
                name: "PaymentReceiptUploadedAt",
                table: "EventRegistrations");

            migrationBuilder.DropColumn(
                name: "PreparationPlan",
                table: "EventRegistrations");
        }
    }
}
