using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Amsterfam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscordWebhookUrl",
                table: "Events",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "NotificationDispatches",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    NewsPostId = table.Column<int>(type: "integer", nullable: true),
                    Action = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    Channel = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    RecipientUserId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    LastError = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    SentAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ExternalId = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDispatches_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_NotificationDispatches_NewsPosts_NewsPostId",
                        column: x => x.NewsPostId,
                        principalTable: "NewsPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                    table.ForeignKey(
                        name: "FK_NotificationDispatches_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_EventId",
                table: "NotificationDispatches",
                column: "EventId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_NewsPostId",
                table: "NotificationDispatches",
                column: "NewsPostId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_RecipientUserId",
                table: "NotificationDispatches",
                column: "RecipientUserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_Status_NextAttemptAt",
                table: "NotificationDispatches",
                columns: new[] { "Status", "NextAttemptAt" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NotificationDispatches");

            migrationBuilder.DropColumn(name: "DiscordWebhookUrl", table: "Events");
        }
    }
}
