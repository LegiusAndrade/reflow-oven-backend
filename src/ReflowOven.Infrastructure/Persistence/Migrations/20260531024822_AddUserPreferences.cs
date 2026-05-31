using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReflowOven.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill existing rows with the real defaults (Theme.System = 2 + the default chart-series
            // visibility) — an empty "{}" would otherwise materialize as Theme.Light + all-false.
            migrationBuilder.AddColumn<string>(
                name: "Preferences",
                table: "Users",
                type: "jsonb",
                nullable: false,
                defaultValue: "{\"Theme\":2,\"Alvo\":true,\"Oven\":true,\"Board\":false,\"Current\":true,\"Voltage\":true,\"OvenFan\":false,\"BoardFan\":false}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Preferences",
                table: "Users");
        }
    }
}
