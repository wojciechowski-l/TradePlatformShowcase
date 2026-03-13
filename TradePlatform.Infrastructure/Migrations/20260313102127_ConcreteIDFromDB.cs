using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConcreteIDFromDB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "AccountIdSeq",
                startValue: 10000L);

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "Accounts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValueSql: "CONCAT('ACC-', CAST(NEXT VALUE FOR AccountIdSeq AS VARCHAR(10)))",
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "AccountIdSeq");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "Accounts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldDefaultValueSql: "CONCAT('ACC-', CAST(NEXT VALUE FOR AccountIdSeq AS VARCHAR(10)))");
        }
    }
}
