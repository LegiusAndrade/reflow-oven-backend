using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReflowOven.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUsernameChars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_Name",
                table: "Users",
                sql: "\"Name\" ~ '^[[:alnum:].]+$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_Name",
                table: "Users");
        }
    }
}
