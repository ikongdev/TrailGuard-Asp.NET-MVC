using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicProfileIdToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: add the column nullable, with no default - the scaffolded
            // version of this migration added it NOT NULL with a single baked-in
            // Guid.Empty default applied identically to every existing row, which
            // would have made every pre-existing account share the same
            // PublicProfileId (and made the unique index below impossible to create
            // with more than one existing user). Hand-edited into four explicit
            // steps so every existing row gets its own distinct value before
            // NOT NULL/uniqueness are enforced.
            migrationBuilder.AddColumn<Guid>(
                name: "PublicProfileId",
                table: "Users",
                type: "uuid",
                nullable: true);

            // Step 2: backfill each existing row with its own value. gen_random_uuid()
            // is evaluated once per row (it is a volatile function, so Postgres cannot
            // fold it into a single constant), and has been a built-in function
            // requiring no extension since PostgreSQL 13 - confirmed available on this
            // project's PostgreSQL 18 target without enabling pgcrypto/uuid-ossp.
            migrationBuilder.Sql(
                "UPDATE \"Users\" SET \"PublicProfileId\" = gen_random_uuid() WHERE \"PublicProfileId\" IS NULL;");

            // Step 3: only now enforce NOT NULL, once every row is guaranteed to hold a
            // distinct, non-empty value.
            migrationBuilder.AlterColumn<Guid>(
                name: "PublicProfileId",
                table: "Users",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // Step 4: unique index, safe now that step 2 guarantees no duplicates.
            migrationBuilder.CreateIndex(
                name: "IX_Users_PublicProfileId",
                table: "Users",
                column: "PublicProfileId",
                unique: true);
        }

        /// <inheritdoc />
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
