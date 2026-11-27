using InventoryAPI.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryAPI.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261127165500_FixRefreshSessionModelMetadata")]
public partial class FixRefreshSessionModelMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_RefreshSessions_CompanyId",
            table: "RefreshSessions",
            column: "CompanyId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_RefreshSessions_CompanyId",
            table: "RefreshSessions");
    }
}
