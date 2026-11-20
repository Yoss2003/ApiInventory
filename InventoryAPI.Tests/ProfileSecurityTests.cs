using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Repositories;
using InventoryAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Tests;

public sealed class ProfileSecurityTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task GetProfileByIdAsync_DoesNotExposeSmtpPassword_AndIsolatesByCompany()
    {
        await using var db = CreateContext();
        db.Profiles.Add(new Profile
        {
            Id = 1,
            CompanyId = 1,
            Username = "admin",
            SmtpEmail = "soporte@empresa.com",
            SmtpPassword = "SuperSecretPassword123!"
        });
        await db.SaveChangesAsync();

        var service = new ProfileService(new WorkFlow(db));

        var own = await service.GetProfileByIdAsync(1, companyId: 1);
        var foreign = await service.GetProfileByIdAsync(1, companyId: 2);

        Assert.NotNull(own);
        Assert.True(own.HasSmtpPassword);
        Assert.Null(typeof(ProfileReadDto).GetProperty("SmtpPassword"));
        Assert.Null(foreign);
    }

    [Fact]
    public async Task UpdateProfileAsync_PreservesExistingSmtpPassword_WhenIncomingPasswordIsEmpty()
    {
        await using var db = CreateContext();
        db.Profiles.Add(new Profile
        {
            Id = 2,
            CompanyId = 1,
            Username = "admin",
            SmtpEmail = "old@empresa.com",
            SmtpPassword = "OriginalSecretPassword!"
        });
        await db.SaveChangesAsync();

        var service = new ProfileService(new WorkFlow(db));
        var update = new ProfileWriteDto
        {
            Id = 2,
            SmtpEmail = "new@empresa.com",
            SmtpPassword = ""
        };

        var result = await service.UpdateProfileAsync(2, update, companyId: 1);

        Assert.Equal(ServiceStatus.NoContent, result.Status);

        var stored = await db.Profiles.AsNoTracking().FirstAsync(p => p.Id == 2);
        Assert.Equal("new@empresa.com", stored.SmtpEmail);
        Assert.Equal("OriginalSecretPassword!", stored.SmtpPassword);
    }
}