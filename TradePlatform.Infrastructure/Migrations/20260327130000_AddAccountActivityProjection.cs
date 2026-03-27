using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountActivityProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountActivityProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CounterpartyAccountId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastEventUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountActivityProjections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountActivityProjections_AccountId_CreatedAtUtc",
                table: "AccountActivityProjections",
                columns: new[] { "AccountId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountActivityProjections_TransactionId_AccountId_Direction",
                table: "AccountActivityProjections",
                columns: new[] { "TransactionId", "AccountId", "Direction" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountActivityProjections");
        }
    }
}
