using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReflowOven.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutotuneRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutotuneRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TargetTempC = table.Column<double>(type: "double precision", nullable: false),
                    Cycles = table.Column<int>(type: "integer", nullable: false),
                    Ku = table.Column<double>(type: "double precision", nullable: true),
                    TuMs = table.Column<int>(type: "integer", nullable: true),
                    Kp = table.Column<double>(type: "double precision", nullable: true),
                    Ki = table.Column<double>(type: "double precision", nullable: true),
                    Kd = table.Column<double>(type: "double precision", nullable: true),
                    PrevKp = table.Column<double>(type: "double precision", nullable: false),
                    PrevKi = table.Column<double>(type: "double precision", nullable: false),
                    PrevKd = table.Column<double>(type: "double precision", nullable: false),
                    Applied = table.Column<bool>(type: "boolean", nullable: false),
                    Dismissed = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FaultCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutotuneRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutotuneRuns_StartedAt",
                table: "AutotuneRuns",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutotuneRuns");
        }
    }
}
