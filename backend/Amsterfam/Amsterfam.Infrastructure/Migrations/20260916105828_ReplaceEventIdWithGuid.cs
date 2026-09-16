using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Amsterfam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceEventIdWithGuid : Migration
    {
        private sealed record EventIndex(string Name, string[] Columns, bool Unique);

        private static readonly (string Table, EventIndex Index)[] DependentTables =
        [
            ("Accommodations", new EventIndex("IX_Accommodations_EventId", ["EventId"], false)),
            ("Activities", new EventIndex("IX_Activities_EventId", ["EventId"], false)),
            (
                "AvailabilityEntries",
                new EventIndex(
                    "IX_AvailabilityEntries_EventId_UserId_Date",
                    ["EventId", "UserId", "Date"],
                    true
                )
            ),
            (
                "EventAttendances",
                new EventIndex("IX_EventAttendances_EventId_UserId", ["EventId", "UserId"], true)
            ),
            (
                "EventComfortQuestions",
                new EventIndex(
                    "IX_EventComfortQuestions_EventId_TemplateId",
                    ["EventId", "TemplateId"],
                    true
                )
            ),
            ("ShoppingItems", new EventIndex("IX_ShoppingItems_EventId", ["EventId"], false)),
            ("ItineraryEntries", new EventIndex("IX_ItineraryEntries_EventId", ["EventId"], false)),
            (
                "DatePollEntries",
                new EventIndex(
                    "IX_DatePollEntries_EventId_UserId_WeekStart",
                    ["EventId", "UserId", "WeekStart"],
                    true
                )
            ),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Event.Id switches from a sequential int to a Guid so event URLs stop being
            // guessable (see issue #93). Existing rows and relationships are preserved:
            // each Event gets a freshly generated Guid, and every dependent table's
            // EventId is backfilled to match its (former) parent before the old int
            // columns are dropped.
            migrationBuilder.AddColumn<Guid>(
                name: "IdNew",
                table: "Events",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()"
            );

            foreach (var (table, index) in DependentTables)
            {
                migrationBuilder.AddColumn<Guid>(
                    name: "EventIdNew",
                    table: table,
                    type: "uuid",
                    nullable: true
                );

                migrationBuilder.Sql(
                    $"""
                    UPDATE "{table}" t SET "EventIdNew" = e."IdNew"
                    FROM "Events" e WHERE t."EventId" = e."Id";
                    """
                );

                migrationBuilder.AlterColumn<Guid>(
                    name: "EventIdNew",
                    table: table,
                    type: "uuid",
                    nullable: false,
                    oldClrType: typeof(Guid),
                    oldType: "uuid",
                    oldNullable: true
                );

                migrationBuilder.DropForeignKey(name: $"FK_{table}_Events_EventId", table: table);
                migrationBuilder.DropIndex(name: index.Name, table: table);
                migrationBuilder.DropColumn(name: "EventId", table: table);
                migrationBuilder.RenameColumn(name: "EventIdNew", table: table, newName: "EventId");
            }

            migrationBuilder.DropPrimaryKey(name: "PK_Events", table: "Events");
            migrationBuilder.DropColumn(name: "Id", table: "Events");
            migrationBuilder.RenameColumn(name: "IdNew", table: "Events", newName: "Id");
            migrationBuilder.AddPrimaryKey(name: "PK_Events", table: "Events", column: "Id");

            foreach (var (table, index) in DependentTables)
            {
                migrationBuilder.AddForeignKey(
                    name: $"FK_{table}_Events_EventId",
                    table: table,
                    column: "EventId",
                    principalTable: "Events",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );

                migrationBuilder.CreateIndex(
                    name: index.Name,
                    table: table,
                    columns: index.Columns,
                    unique: index.Unique
                );
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort only: there is no meaningful guid->sequential-int mapping to
            // restore, so rows are kept but their EventId relationships are zeroed
            // rather than the tables being wiped.
            foreach (var (table, index) in DependentTables)
            {
                migrationBuilder.DropForeignKey(name: $"FK_{table}_Events_EventId", table: table);
                migrationBuilder.DropIndex(name: index.Name, table: table);

                migrationBuilder.Sql(
                    $"""ALTER TABLE "{table}" ALTER COLUMN "EventId" TYPE integer USING 0;"""
                );
            }

            migrationBuilder.DropPrimaryKey(name: "PK_Events", table: "Events");

            migrationBuilder.Sql(
                """
                ALTER TABLE "Events" ALTER COLUMN "Id" DROP DEFAULT;
                ALTER TABLE "Events" ALTER COLUMN "Id" TYPE integer USING 0;
                """
            );

            migrationBuilder.AddPrimaryKey(name: "PK_Events", table: "Events", column: "Id");

            migrationBuilder.Sql(
                """ALTER TABLE "Events" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY;"""
            );

            foreach (var (table, index) in DependentTables)
            {
                // Every zeroed EventId is now dangling (no Events.Id is ever 0), so the
                // constraint is added NOT VALID: it applies to future writes without
                // requiring the already-severed historical rows to satisfy it.
                migrationBuilder.Sql(
                    $"""
                    ALTER TABLE "{table}" ADD CONSTRAINT "FK_{table}_Events_EventId"
                    FOREIGN KEY ("EventId") REFERENCES "Events" ("Id") ON DELETE CASCADE NOT VALID;
                    """
                );

                migrationBuilder.CreateIndex(
                    name: index.Name,
                    table: table,
                    columns: index.Columns,
                    unique: index.Unique
                );
            }
        }
    }
}
