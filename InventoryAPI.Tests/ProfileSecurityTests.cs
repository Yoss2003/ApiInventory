using System.Security.Cryptography;
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
    public async Task UpdateProfileAsync_EncryptsEvenWhenInputStartsWithPrefix_AndRejectsTamperedCiphertext()
    {
        await using var db = CreateContext();
        var service = new ProfileService(new WorkFlow(db), Protector);

        var prefixLikeInput = "enc:v1:userTypedPassword";
        var createResult = await service.CreateProfileAsync(new ProfileWriteDto
        {
            Username = "admin",
            SmtpEmail = "old@empresa.com",
            SmtpPassword = prefixLikeInput
        }, companyId: 1);

        Assert.Equal(ServiceStatus.Created, createResult.Status);
        var created = Assert.IsType<ProfileReadDto>(createResult.Payload);

        var storedAfterCreate = await db.Profiles.AsNoTracking().FirstAsync(p => p.Id == created.Id);
        Assert.NotEqual(prefixLikeInput, storedAfterCreate.SmtpPassword);
        Assert.True(Protector.TryUnprotect(storedAfterCreate.SmtpPassword, out var plain));
        Assert.Equal(prefixLikeInput, plain);

        var tampered = storedAfterCreate.SmtpPassword![..^2] + "AA";
        Assert.False(Protector.TryUnprotect(tampered, out _));
        Assert.Throws<CryptographicException>(() => Protector.Unprotect(tampered));

        var otherProtector = new AesGcmSecretProtector("CompletelyDifferentKeyWith32CharsMinimum!!");
        Assert.False(otherProtector.TryUnprotect(storedAfterCreate.SmtpPassword, out _));
    }
    [Fact]
    public void ProtectStoredValue_RejectsCiphertextThatCannotBeAuthenticated()
    {
        var encrypted = Protector.Protect("SMTP-secret-123!");
        var tampered = encrypted[..^2] + "AA";

        Assert.Throws<CryptographicException>(() => Protector.ProtectStoredValue(tampered));

        var wrongKeyProtector = new AesGcmSecretProtector("AnotherMasterEncryptionKeyWith32Chars!");
        Assert.Throws<CryptographicException>(() => wrongKeyProtector.ProtectStoredValue(encrypted));
    }

}