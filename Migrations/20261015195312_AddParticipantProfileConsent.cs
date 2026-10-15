using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddParticipantProfileConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParticipantProfileConsentVersion",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ParticipantProfileConsentedAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParticipantProfileConsentVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ParticipantProfileConsentedAtUtc",
                table: "Users");
        }
    }
}
