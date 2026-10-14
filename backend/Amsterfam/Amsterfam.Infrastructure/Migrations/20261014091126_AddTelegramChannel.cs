using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Amsterfam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TelegramBotToken",
                table: "Events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TelegramBotUsername",
                table: "Events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<long>(
                name: "TelegramChatId",
                table: "Events",
                type: "bigint",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TelegramChatTitle",
                table: "Events",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TelegramBotToken", table: "Events");

            migrationBuilder.DropColumn(name: "TelegramBotUsername", table: "Events");

            migrationBuilder.DropColumn(name: "TelegramChatId", table: "Events");

            migrationBuilder.DropColumn(name: "TelegramChatTitle", table: "Events");
        }
    }
}
