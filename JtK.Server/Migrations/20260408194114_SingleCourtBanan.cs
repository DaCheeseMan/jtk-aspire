using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace JtK.Server.Migrations
{
    /// <inheritdoc />
    public partial class SingleCourtBanan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Courts",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Courts",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.UpdateData(
                table: "Courts",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Description", "Name", "Surface" },
                values: new object[] { "Utomhusbana med asfaltunderlag.", "Banan", "Asfalt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Courts",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Description", "Name", "Surface" },
                values: new object[] { "Utomhusbana med grusunderlag.", "Bana 1", "Clay" });

            migrationBuilder.InsertData(
                table: "Courts",
                columns: new[] { "Id", "Description", "IsActive", "Name", "Surface" },
                values: new object[,]
                {
                    { 2, "Utomhusbana med grusunderlag.", true, "Bana 2", "Clay" },
                    { 3, "Inomhusbana med hårt underlag.", true, "Bana 3", "Hard" }
                });
        }
    }
}
