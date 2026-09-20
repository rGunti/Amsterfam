using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Amsterfam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class JoinLinkLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "EventJoinLinks",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "JoinLinkId",
                table: "EventAttendances",
                type: "integer",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventAttendances_JoinLinkId",
                table: "EventAttendances",
                column: "JoinLinkId"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_EventAttendances_EventJoinLinks_JoinLinkId",
                table: "EventAttendances",
                column: "JoinLinkId",
                principalTable: "EventJoinLinks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EventAttendances_EventJoinLinks_JoinLinkId",
                table: "EventAttendances"
            );

            migrationBuilder.DropIndex(
                name: "IX_EventAttendances_JoinLinkId",
                table: "EventAttendances"
            );

            migrationBuilder.DropColumn(name: "Label", table: "EventJoinLinks");

            migrationBuilder.DropColumn(name: "JoinLinkId", table: "EventAttendances");
        }
    }
}
