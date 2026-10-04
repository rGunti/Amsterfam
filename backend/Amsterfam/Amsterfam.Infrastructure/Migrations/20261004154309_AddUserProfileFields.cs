using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Amsterfam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bio",
                table: "Users",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true
            );

            migrationBuilder.AddColumn<short>(
                name: "BirthYear",
                table: "Users",
                type: "smallint",
                nullable: true
            );

            migrationBuilder.AddColumn<short>(
                name: "BirthdayDay",
                table: "Users",
                type: "smallint",
                nullable: true
            );

            migrationBuilder.AddColumn<short>(
                name: "BirthdayMonth",
                table: "Users",
                type: "smallint",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "DietaryNotes",
                table: "Users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Pronouns",
                table: "Users",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "DietaryOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DietaryOptions", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "UserDietaryOptions",
                columns: table => new
                {
                    DietaryOptionsId = table.Column<int>(type: "integer", nullable: false),
                    UsersId = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_UserDietaryOptions",
                        x => new { x.DietaryOptionsId, x.UsersId }
                    );
                    table.ForeignKey(
                        name: "FK_UserDietaryOptions_DietaryOptions_DietaryOptionsId",
                        column: x => x.DietaryOptionsId,
                        principalTable: "DietaryOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_UserDietaryOptions_Users_UsersId",
                        column: x => x.UsersId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.InsertData(
                table: "DietaryOptions",
                columns: new[] { "Id", "Key", "Label", "SortOrder" },
                values: new object[,]
                {
                    { 1, "vegetarian", "Vegetarian", 10 },
                    { 2, "vegan", "Vegan", 20 },
                    { 3, "pescatarian", "Pescatarian", 30 },
                    { 4, "halal", "Halal", 40 },
                    { 5, "kosher", "Kosher", 50 },
                    { 6, "no-pork", "No pork", 60 },
                    { 7, "no-alcohol", "No alcohol", 70 },
                    { 8, "gluten-free", "Gluten-free", 80 },
                    { 9, "lactose-free", "Lactose-free", 90 },
                    { 10, "nut-allergy", "Nut allergy", 100 },
                    { 11, "peanut-allergy", "Peanut allergy", 110 },
                    { 12, "shellfish-allergy", "Shellfish allergy", 120 },
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_DietaryOptions_Key",
                table: "DietaryOptions",
                column: "Key",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserDietaryOptions_UsersId",
                table: "UserDietaryOptions",
                column: "UsersId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserDietaryOptions");

            migrationBuilder.DropTable(name: "DietaryOptions");

            migrationBuilder.DropColumn(name: "Bio", table: "Users");

            migrationBuilder.DropColumn(name: "BirthYear", table: "Users");

            migrationBuilder.DropColumn(name: "BirthdayDay", table: "Users");

            migrationBuilder.DropColumn(name: "BirthdayMonth", table: "Users");

            migrationBuilder.DropColumn(name: "DietaryNotes", table: "Users");

            migrationBuilder.DropColumn(name: "Location", table: "Users");

            migrationBuilder.DropColumn(name: "Pronouns", table: "Users");
        }
    }
}
