using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyLangApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KnowledgeItems_DueAt",
                table: "KnowledgeItems");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeItems_PhraseNormalized",
                table: "KnowledgeItems");

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "KnowledgeItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Episodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            // Данные, созданные до появления пользователей (V1–V4), не теряем: передаём их
            // служебному пользователю legacy@localhost. Войти под ним нельзя — хеш пароля пустой.
            migrationBuilder.Sql("""
                INSERT INTO "Users" ("Email", "PasswordHash", "CreatedAt")
                SELECT 'legacy@localhost', '', strftime('%Y-%m-%d %H:%M:%f', 'now')
                WHERE EXISTS (SELECT 1 FROM "Episodes") OR EXISTS (SELECT 1 FROM "KnowledgeItems");

                UPDATE "Episodes" SET "UserId" = (SELECT "Id" FROM "Users" WHERE "Email" = 'legacy@localhost')
                WHERE "UserId" = 0;

                UPDATE "KnowledgeItems" SET "UserId" = (SELECT "Id" FROM "Users" WHERE "Email" = 'legacy@localhost')
                WHERE "UserId" = 0;
                """);

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeItems_UserId_DueAt",
                table: "KnowledgeItems",
                columns: new[] { "UserId", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeItems_UserId_PhraseNormalized",
                table: "KnowledgeItems",
                columns: new[] { "UserId", "PhraseNormalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_UserId",
                table: "Episodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Episodes_Users_UserId",
                table: "Episodes",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeItems_Users_UserId",
                table: "KnowledgeItems",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Episodes_Users_UserId",
                table: "Episodes");

            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeItems_Users_UserId",
                table: "KnowledgeItems");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeItems_UserId_DueAt",
                table: "KnowledgeItems");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeItems_UserId_PhraseNormalized",
                table: "KnowledgeItems");

            migrationBuilder.DropIndex(
                name: "IX_Episodes_UserId",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "KnowledgeItems");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Episodes");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeItems_DueAt",
                table: "KnowledgeItems",
                column: "DueAt");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeItems_PhraseNormalized",
                table: "KnowledgeItems",
                column: "PhraseNormalized",
                unique: true);
        }
    }
}
