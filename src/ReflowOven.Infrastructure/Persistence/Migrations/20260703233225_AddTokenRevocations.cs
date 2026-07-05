using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReflowOven.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenRevocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TokenRevocations",
                columns: table => new
                {
                    Subject = table.Column<Guid>(type: "uuid", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenRevocations", x => x.Subject);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TokenRevocations");
        }
    }
}
