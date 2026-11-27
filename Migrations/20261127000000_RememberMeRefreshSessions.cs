using InventoryAPI.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryAPI.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261127000000_RememberMeRefreshSessions")]
public partial class RememberMeRefreshSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RefreshSessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<int>(type: "integer", nullable: false),
                CompanyId = table.Column<int>(type: "integer", nullable: false),
                TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RememberMe = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RefreshSessions", x => x.Id);
                table.ForeignKey("FK_RefreshSessions_Users_UserId", x => x.UserId, "Users", "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_RefreshSessions_Companies_CompanyId", x => x.CompanyId, "Companies", "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_RefreshSessions_TokenHash", "RefreshSessions", "TokenHash", unique: true);
        migrationBuilder.CreateIndex("IX_RefreshSessions_UserId_CompanyId_ExpiresAt", "RefreshSessions",
            new[] { "UserId", "CompanyId", "ExpiresAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "RefreshSessions");
    }
}
