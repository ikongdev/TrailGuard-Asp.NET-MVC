using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddParticipantEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // False is the deliberate rollout default: no existing account is
            // backfilled as verified or newly required to verify before sign-in.
            migrationBuilder.AddColumn<bool>(
                name: "ParticipantEmailVerificationRequired",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEmailVerificationSentAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingEmailChange",
                table: "Users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PendingEmailChangeRequestedAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ParticipantEmailVerificationRequired", table: "Users");
            migrationBuilder.DropColumn(name: "LastEmailVerificationSentAtUtc", table: "Users");
            migrationBuilder.DropColumn(name: "PendingEmailChange", table: "Users");
            migrationBuilder.DropColumn(name: "PendingEmailChangeRequestedAtUtc", table: "Users");
        }
    }
}
