using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Security;
using InventoryAPI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace InventoryAPI.Tests;

public sealed class UserCredentialMailerTenantTests
{
    private sealed class TestProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string ProtectStoredValue(string? storedValue) => storedValue ?? string.Empty;
        public bool TryUnprotect(string? storedValue, out string plaintext)
        {
            plaintext = storedValue ?? string.Empty;
            return !string.IsNullOrWhiteSpace(storedValue);
        }
        public string Unprotect(string storedValue) => storedValue;
        public bool IsProtected(string? storedValue) => !string.IsNullOrWhiteSpace(storedValue);
    }

    [Fact]
    public async Task IsConfiguredAsync_DoesNotUseCentralCompanyProfileAsTenantFallback()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        db.Profiles.Add(new Profile
        {
            Id = 1,
            CompanyId = 1,
            Username = "central-admin",
            SmtpEmail = "central@example.test",
            SmtpPassword = "central-secret"
        });
        await db.SaveChangesAsync();

        var mailer = new UserCredentialMailer(
            db,
            new ConfigurationBuilder().Build(),
            new TestProtector(),
            NullLogger<UserCredentialMailer>.Instance);

        Assert.True(await mailer.IsConfiguredAsync(1));
        Assert.False(await mailer.IsConfiguredAsync(2));
        Assert.False(await mailer.IsConfiguredAsync(0));
    }

    [Fact]
    public async Task IsConfiguredAsync_AllowsTheTenantOwnSmtpProfile()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        db.Profiles.Add(new Profile
        {
            Id = 2,
            CompanyId = 2,
            Username = "tenant-admin",
            SmtpEmail = "tenant@example.test",
            SmtpPassword = "tenant-secret"
        });
        await db.SaveChangesAsync();

        var mailer = new UserCredentialMailer(
            db,
            new ConfigurationBuilder().Build(),
            new TestProtector(),
            NullLogger<UserCredentialMailer>.Instance);

        Assert.True(await mailer.IsConfiguredAsync(2));
        Assert.False(await mailer.IsConfiguredAsync(1));
    }
}