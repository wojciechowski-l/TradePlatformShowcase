using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IntroduceTransactionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "Transactions",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingStartedAtUtc",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidatedAtUtc",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Balance",
                table: "Accounts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "AccountActivityProjections",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ProcessingStartedAtUtc",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ValidatedAtUtc",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Balance",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "AccountActivityProjections");
        }
    }
}
