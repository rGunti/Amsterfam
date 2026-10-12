using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Amsterfam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNewsFeedTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NewsFeedTokens",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    CreatedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    RevokedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    LastUsedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsFeedTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewsFeedTokens_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_NewsFeedTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_NewsFeedTokens_EventId",
                table: "NewsFeedTokens",
                column: "EventId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NewsFeedTokens_TokenHash",
                table: "NewsFeedTokens",
                column: "TokenHash",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_NewsFeedTokens_UserId_EventId",
                table: "NewsFeedTokens",
                columns: new[] { "UserId", "EventId" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NewsFeedTokens");
        }
    }
}
