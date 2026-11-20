using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Security;
using InventoryAPI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace InventoryAPI.Tests;

public sealed class UserServiceTenantIsolationTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static UserService CreateService(AppDbContext db, ISecretProtector? protector = null)
    {
        return new UserService(
            new WorkFlow(db),
            new ConfigurationBuilder().Build(),
            protector ?? new AesGcmSecretProtector("UserServiceTenantTestEncryptionKey_32Chars!"));
    }

    [Fact]
    public async Task UserOperations_DoNotReadOrModifyUsersFromAnotherCompany()
    {
        await using var db = CreateContext();
        db.Users.Add(new User
        {
            Id = 25,
            Username = "employee-company-2",
            Email = "employee@company-two.test",
            CompanyId = 2,
            RoleId = 3,
            IsActive = true,
            StatusId = 2,
            MustChangePassword = true,
            IsTwoFactorEnabled = true,
            TwoFactorSecret = "JBSWY3DPEHPK3PXP"
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        Assert.Null(await service.GetUserDtoByIdAsync(25, companyId: 1));

        var update = await service.UpdateUserAsync(25, new User
        {
            Id = 25,
            CompanyId = 1,
            Username = "attacker",
            Email = "attacker@company-one.test",
            RoleId = 3,
            IsActive = false
        }, companyId: 1);

        Assert.False(update.Success);
        Assert.False((await service.ApproveEmployeeAsync(25, companyId: 1)).Success);
        Assert.False((await service.UpdatePhotoAsync(25, companyId: 1, "not-base64", Path.GetTempPath())).Success);
        Assert.False((await service.Generate2FAAsync(25, companyId: 1)).Success);
        Assert.False(await service.Enable2FAAsync(25, companyId: 1, "123456"));
        Assert.False(await service.Disable2FAAsync(25, companyId: 1));
        Assert.False(await service.UpdateFcmTokenAsync(25, companyId: 1, "foreign-fid"));
        Assert.False((await service.ResendWelcomeEmailAsync(25, companyId: 1)).Success);
        Assert.False((await service.ChangeInitialPasswordAsync(25, companyId: 1, "NewPassword123!")).Success);
        Assert.False(await service.DeleteUserInCompanyAsync(25, companyId: 1, "1"));

        var unchanged = await db.Users.AsNoTracking().SingleAsync(u => u.Id == 25);
        Assert.Equal(2, unchanged.CompanyId.GetValueOrDefault());
        Assert.Equal("employee@company-two.test", unchanged.Email);
        Assert.Equal("employee-company-2", unchanged.Username);
        Assert.Equal(2, unchanged.StatusId);
        Assert.True(unchanged.IsActive);
        Assert.True(unchanged.IsTwoFactorEnabled);
        Assert.True(unchanged.MustChangePassword);
    }

    [Fact]
    public async Task ApproveEmployee_DoesNotUseSmtpConfigurationFromAnotherCompany()
    {
        await using var db = CreateContext();
        db.Users.Add(new User
        {
            Id = 41,
            Username = "employee-company-2",
            Email = "employee@company-two.test",
            CompanyId = 2,
            RoleId = 3,
            IsActive = true,
            StatusId = 2
        });
        db.Profiles.Add(new Profile
        {
            Id = 10,
            Username = "company-one-admin",
            CompanyId = 1,
            SmtpEmail = "smtp@company-one.test",
            SmtpPassword = "company-one-secret"
        });
        await db.SaveChangesAsync();

        var protector = new RejectUnexpectedUnprotectSecretProtector();
        var service = CreateService(db, protector);

        var result = await service.ApproveEmployeeAsync(41, companyId: 2);

        Assert.True(result.Success);

        var resend = await service.ResendWelcomeEmailAsync(41, companyId: 2);
        Assert.False(resend.Success);
        Assert.False(protector.UnprotectWasCalled);
        Assert.Equal(1, await db.Users.Where(u => u.Id == 41 && u.StatusId == 1).CountAsync());
    }

    private sealed class RejectUnexpectedUnprotectSecretProtector : ISecretProtector
    {
        public bool UnprotectWasCalled { get; private set; }

        public string Protect(string plaintext) => throw new NotSupportedException();
        public string ProtectStoredValue(string? storedValue) => throw new NotSupportedException();
        public bool TryUnprotect(string? storedValue, out string plaintext)
        {
            plaintext = string.Empty;
            throw new NotSupportedException();
        }
        public string Unprotect(string storedValue)
        {
            UnprotectWasCalled = true;
            throw new InvalidOperationException("No debe utilizarse el SMTP de otra empresa.");
        }
        public bool IsProtected(string? storedValue) => false;
    }
}
