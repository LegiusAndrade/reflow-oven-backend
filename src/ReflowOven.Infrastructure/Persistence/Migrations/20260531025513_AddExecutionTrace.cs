using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReflowOven.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionTrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pre-existing executions have no captured trace: a valid EMPTY snapshot (not "{}", which would
            // materialize Series as null) so the detail mapping renders "no trace" instead of throwing.
            migrationBuilder.AddColumn<string>(
                name: "Trace",
                table: "Executions",
                type: "jsonb",
                nullable: false,
                defaultValue: "{\"DurationSec\":0,\"Series\":[]}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Trace",
                table: "Executions");
        }
    }
}
