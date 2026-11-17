using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace orderManagementSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPaymentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayMongoCheckoutSessionId",
                table: "Orders",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayMongoCheckoutUrl",
                table: "Orders",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayMongoPaymentId",
                table: "Orders",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "Orders",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Cash");

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentPaidAtUtc",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "Orders",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotTracked");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayMongoCheckoutSessionId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayMongoCheckoutUrl",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayMongoPaymentId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentPaidAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Orders");
        }
    }
}
