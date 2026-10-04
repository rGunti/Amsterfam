using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Amsterfam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAuthSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthSource",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_Users_Handle_AuthSource",
                    table: "Users",
                    columns: new[] { "Handle", "AuthSource" },
                    unique: true
                )
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Users_Handle_AuthSource", table: "Users");

            migrationBuilder.DropColumn(name: "AuthSource", table: "Users");
        }
    }
}
