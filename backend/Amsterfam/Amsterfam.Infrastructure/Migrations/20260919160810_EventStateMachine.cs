using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Amsterfam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EventStateMachine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoTransitionsPaused",
                table: "Events",
                type: "boolean",
                nullable: false,
                defaultValue: false
            );

            // Issue #107: the old Draft/Open/Closed meanings don't map onto the new
            // lifecycle, so every existing event restarts as Draft.
            migrationBuilder.Sql(@"UPDATE ""Events"" SET ""Status"" = 'Draft';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Map new-only states back onto the old three-state enum.
            migrationBuilder.Sql(
                @"UPDATE ""Events"" SET ""Status"" = 'Open' WHERE ""Status"" IN ('LookingForDate', 'InProgress');"
            );
            migrationBuilder.Sql(
                @"UPDATE ""Events"" SET ""Status"" = 'Closed' WHERE ""Status"" IN ('Archived', 'Cancelled');"
            );

            migrationBuilder.DropColumn(name: "AutoTransitionsPaused", table: "Events");
        }
    }
}
