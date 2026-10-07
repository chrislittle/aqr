using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqr.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class LiveApiShapes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RetirementDate",
                schema: "dim",
                table: "VmFamily",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawName",
                schema: "quota",
                table: "SubscriptionQuota",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetirementDate",
                schema: "dim",
                table: "VmFamily");

            migrationBuilder.DropColumn(
                name: "RawName",
                schema: "quota",
                table: "SubscriptionQuota");
        }
    }
}
