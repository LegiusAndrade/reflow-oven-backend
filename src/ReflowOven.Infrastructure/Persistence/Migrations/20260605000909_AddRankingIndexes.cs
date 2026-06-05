using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReflowOven.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRankingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Users_LoginCount",
                table: "Users",
                column: "LoginCount");

            migrationBuilder.CreateIndex(
                name: "IX_Programs_RunCount",
                table: "Programs",
                column: "RunCount");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_LoginCount",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Programs_RunCount",
                table: "Programs");
        }
    }
}
