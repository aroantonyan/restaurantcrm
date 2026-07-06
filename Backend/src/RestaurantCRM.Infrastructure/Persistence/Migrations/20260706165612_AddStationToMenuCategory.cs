using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantCRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStationToMenuCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Station",
                table: "MenuCategories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Kitchen");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Station",
                table: "MenuCategories");
        }
    }
}
