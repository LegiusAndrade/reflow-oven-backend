using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReflowOven.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionFailureLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "Executions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FaultTypeCode",
                table: "Executions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LinkedErrorId",
                table: "Executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Executions_LinkedErrorId",
                table: "Executions",
                column: "LinkedErrorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Executions_LinkedErrorId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "FaultTypeCode",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "LinkedErrorId",
                table: "Executions");
        }
    }
}
