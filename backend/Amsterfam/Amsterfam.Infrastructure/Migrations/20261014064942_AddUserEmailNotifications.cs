using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Amsterfam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserEmailNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailNotifications",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<string>(
                name: "NotificationEmail",
                table: "Users",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NotificationEmailConfirmedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "NotificationEmailTokenHash",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NotificationEmailTokenSentAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "EmailNotifications", table: "Users");

            migrationBuilder.DropColumn(name: "NotificationEmail", table: "Users");

            migrationBuilder.DropColumn(name: "NotificationEmailConfirmedAt", table: "Users");

            migrationBuilder.DropColumn(name: "NotificationEmailTokenHash", table: "Users");

            migrationBuilder.DropColumn(name: "NotificationEmailTokenSentAt", table: "Users");
        }
    }
}
