using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Amsterfam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EventLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventLogEntries",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    Visibility = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    OccurredAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ActorId = table.Column<int>(type: "integer", nullable: true),
                    SubjectUserId = table.Column<int>(type: "integer", nullable: true),
                    Data = table.Column<string>(type: "jsonb", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventLogEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventLogEntries_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_EventLogEntries_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_EventLogEntries_Users_SubjectUserId",
                        column: x => x.SubjectUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventLogEntries_ActorId",
                table: "EventLogEntries",
                column: "ActorId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventLogEntries_EventId_Id",
                table: "EventLogEntries",
                columns: new[] { "EventId", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventLogEntries_SubjectUserId",
                table: "EventLogEntries",
                column: "SubjectUserId"
            );

            // Give existing events a starting point. The actor is left empty: after an
            // ownership transfer CreatedById is no longer who created the event.
            migrationBuilder.Sql(
                """
                INSERT INTO "EventLogEntries" ("EventId", "Type", "Visibility", "OccurredAt")
                SELECT "Id", 'EventCreated', 'Everyone', "CreatedAt" FROM "Events";
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EventLogEntries");
        }
    }
}
