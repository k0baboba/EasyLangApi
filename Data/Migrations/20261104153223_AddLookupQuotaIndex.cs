using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyLangApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLookupQuotaIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Lookups_KnowledgeItemId",
                table: "Lookups");

            migrationBuilder.CreateIndex(
                name: "IX_Lookups_KnowledgeItemId_CreatedAt",
                table: "Lookups",
                columns: new[] { "KnowledgeItemId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Lookups_KnowledgeItemId_CreatedAt",
                table: "Lookups");

            migrationBuilder.CreateIndex(
                name: "IX_Lookups_KnowledgeItemId",
                table: "Lookups",
                column: "KnowledgeItemId");
        }
    }
}
