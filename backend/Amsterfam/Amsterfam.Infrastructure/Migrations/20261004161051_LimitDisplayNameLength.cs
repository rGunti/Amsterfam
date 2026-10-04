using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Amsterfam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LimitDisplayNameLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The API never limited display names before, so cut any longer ones down first
            // rather than failing the column change.
            migrationBuilder.Sql(
                """UPDATE "Users" SET "DisplayName" = left("DisplayName", 100) WHERE length("DisplayName") > 100;"""
            );

            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "Users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true
            );
        }
    }
}
