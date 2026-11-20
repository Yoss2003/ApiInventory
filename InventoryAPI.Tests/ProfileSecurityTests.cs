using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Repositories;
using InventoryAPI.Security;
using InventoryAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Tests;

public sealed class ProfileSecurityTests
{
    private static readonly ISecretProtector Protector =
        new AesGcmSecretProtector("TestMasterEncryptionKeyWith32CharsMinimum!");

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
            SmtpPassword = Protector.Protect("SuperSecretPassword123!")
        });
        await db.SaveChangesAsync();

        var service = new ProfileService(new WorkFlow(db), Protector);

        var own = await service.GetProfileByIdAsync(1, companyId: 1);
        var foreign = await service.GetProfileByIdAsync(1, companyId: 2);

        Assert.NotNull(own);
        Assert.True(own.HasSmtpPassword);
        Assert.Null(typeof(ProfileReadDto).GetProperty("SmtpPassword"));
        Assert.Null(foreign);
    }

    [Fact]
    public async Task UpdateProfileAsync_EncryptsNewPassword_AndPreservesExistingWhenEmpty()
    {
        await using var db = CreateContext();
        var service = new ProfileService(new WorkFlow(db), Protector);

        var createResult = await service.CreateProfileAsync(new ProfileWriteDto
        {
            Username = "admin",
            SmtpEmail = "old@empresa.com",
            SmtpPassword = "OriginalSecretPassword!"
        }, companyId: 1);

        Assert.Equal(ServiceStatus.Created, createResult.Status);
        var created = Assert.IsType<ProfileReadDto>(createResult.Payload);

        var storedAfterCreate = await db.Profiles.AsNoTracking().FirstAsync(p => p.Id == created.Id);
        Assert.NotEqual("OriginalSecretPassword!", storedAfterCreate.SmtpPassword);
        Assert.True(Protector.IsProtected(storedAfterCreate.SmtpPassword));
        Assert.Equal("OriginalSecretPassword!", Protector.Unprotect(storedAfterCreate.SmtpPassword!));

        var updateResult = await service.UpdateProfileAsync(created.Id, new ProfileWriteDto
        {
            Id = created.Id,
            SmtpEmail = "new@empresa.com",
            SmtpPassword = ""
        }, companyId: 1);

        Assert.Equal(ServiceStatus.NoContent, updateResult.Status);

        var storedAfterUpdate = await db.Profiles.AsNoTracking().FirstAsync(p => p.Id == created.Id);
        Assert.Equal("new@empresa.com", storedAfterUpdate.SmtpEmail);
        Assert.Equal("OriginalSecretPassword!", Protector.Unprotect(storedAfterUpdate.SmtpPassword!));
    }
}