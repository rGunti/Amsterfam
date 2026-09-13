using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Amsterfam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeEventDatesOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateOnly>(
                name: "StartDate",
                table: "Events",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date"
            );

            migrationBuilder.AlterColumn<DateOnly>(
                name: "EndDate",
                table: "Events",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateOnly>(
                name: "StartDate",
                table: "Events",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<DateOnly>(
                name: "EndDate",
                table: "Events",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true
            );
        }
    }
}
