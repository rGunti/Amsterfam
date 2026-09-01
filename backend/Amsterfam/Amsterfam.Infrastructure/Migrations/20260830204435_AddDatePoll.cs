using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Amsterfam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDatePoll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "PollRangeEnd",
                table: "Events",
                type: "date",
                nullable: true
            );

            migrationBuilder.AddColumn<DateOnly>(
                name: "PollRangeStart",
                table: "Events",
                type: "date",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "DatePollEntries",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatePollEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DatePollEntries_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_DatePollEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_DatePollEntries_EventId_UserId_WeekStart",
                table: "DatePollEntries",
                columns: new[] { "EventId", "UserId", "WeekStart" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_DatePollEntries_UserId",
                table: "DatePollEntries",
                column: "UserId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DatePollEntries");

            migrationBuilder.DropColumn(name: "PollRangeEnd", table: "Events");

            migrationBuilder.DropColumn(name: "PollRangeStart", table: "Events");
        }
    }
}
