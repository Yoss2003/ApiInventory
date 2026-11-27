using InventoryAPI.Data;
using InventoryAPI.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;

namespace InventoryAPI.Tests;

public sealed class HttpAuthorizationTests : IClassFixture<HttpAuthorizationTests.TestApiFactory>
{
    private const string TestJwtKey = "SuperSecretJwtSigningKeyForHttpTests_32CharsMin!";
    private const string TestIssuer = "ControlInventarioAPI";
    private const string TestAudience = "ControlInventarioMovil";

    private readonly TestApiFactory _factory;
    private Guid _sessionId;

    public HttpAuthorizationTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    private static string BuildJwt(int userId, int companyId, int roleId, DateTime expiresUtc, string signingKey = TestJwtKey, Guid? sessionId = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var utcNow = DateTime.UtcNow;
        var utcExpires = expiresUtc.Kind == DateTimeKind.Utc ? expiresUtc : expiresUtc.ToUniversalTime();
        var utcNotBefore = utcExpires > utcNow ? utcNow.AddMinutes(-5) : utcExpires.AddMinutes(-10);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, $"user{userId}"),
            new("UserId", userId.ToString()),
            new("CompanyId", companyId.ToString()),
            new("RoleId", roleId.ToString()),
            new("iat", new DateTimeOffset(utcNow).ToUnixTimeSeconds().ToString())
        };
        if (sessionId.HasValue) claims.Add(new Claim("sid", sessionId.Value.ToString("D")));

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            notBefore: utcNotBefore,
            expires: utcExpires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task ProtectedEndpoints_WithoutToken_Return401Unauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/Customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoints_WithExpiredJwt_Return401Unauthorized()
    {
        using var client = _factory.CreateClient();
        var expiredToken = BuildJwt(userId: 10, companyId: 1, roleId: 3, expiresUtc: DateTime.UtcNow.AddMinutes(-30));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.GetAsync("/api/Customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoints_WithInvalidSignatureJwt_Return401Unauthorized()
    {
        using var client = _factory.CreateClient();
        var forgedToken = BuildJwt(
            userId: 10,
            companyId: 1,
            roleId: 3,
            expiresUtc: DateTime.UtcNow.AddHours(1),
            signingKey: "CompletelyWrongSigningKeyForTamperingTest_32Chars!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        var response = await client.GetAsync("/api/Customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CustomersEndpoint_AuthenticatedWithoutPermission_Returns403Forbidden()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Companies.AnyAsync(c => c.Id == 1))
            {
                db.Companies.Add(new Company
                {
                    Id = 1,
                    BusinessName = "Empresa Test",
                    Ruc = "20123456789",
                    IsActive = true,
                    RegistrationDate = DateTime.UtcNow
                });
            }

            if (!await db.Roles.AnyAsync(r => r.Id == 5))
            {
                db.Roles.Add(new Role { Id = 5, Name = "SinPermisos" });
            }

            if (!await db.Users.AnyAsync(u => u.Id == 50))
            {
                db.Users.Add(new User
                {
                    Id = 50,
                    Username = "lector_sin_permisos",
                    Email = "lector@test.com",
                    RoleId = 5,
                    CompanyId = 1,
                    IsActive = true,
                    StatusId = 1
                });
            }

            if (!await db.CompanyLicenses.AnyAsync(l => l.CompanyId == 1))
            {
                db.CompanyLicenses.Add(new CompanyLicense
                {
                    CompanyId = 1,
                    Plan = "Básico",
                    MaxUsers = 5,
                    StartsAt = DateTime.UtcNow.AddDays(-1),
                    IsEnabled = true,
                    ExpiresAt = DateTime.UtcNow.AddDays(30),
                    UpdatedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();

            var sessionId = Guid.NewGuid();
            db.RefreshSessions.Add(new RefreshSession
            {
                Id = sessionId,
                UserId = 50,
                CompanyId = 1,
                TokenHash = new string('A', 64),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                RememberMe = false
            });
            await db.SaveChangesAsync();
            _sessionId = sessionId;
        }

        using var client = _factory.CreateClient();
        var token = BuildJwt(userId: 50, companyId: 1, roleId: 5,
            expiresUtc: DateTime.UtcNow.AddHours(1), sessionId: _sessionId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/Customers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbName = $"HttpAuthTests_{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=unused_in_tests");
            builder.UseSetting("Jwt:Key", TestJwtKey);
            builder.UseSetting("Jwt:Issuer", TestIssuer);
            builder.UseSetting("Jwt:Audience", TestAudience);
            builder.UseSetting("Security:EncryptionKey", "SeparateEncryptionKeyForHttpTests_32CharsMin!");
            builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:4200");

            builder.ConfigureServices(services =>
            {
                var dbContextDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (dbContextDescriptor is not null)
                    services.Remove(dbContextDescriptor);

                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase(_dbName));

                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var prevOnFail = options.Events.OnAuthenticationFailed;
                    options.Events.OnAuthenticationFailed = async ctx =>
                    {
                        Console.WriteLine($"[JWT_AUTH_FAILED] {ctx.Exception.GetType().Name}: {ctx.Exception.Message}");
                        if (prevOnFail is not null) await prevOnFail(ctx);
                    };

                    var prevOnChallenge = options.Events.OnChallenge;
                    options.Events.OnChallenge = async ctx =>
                    {
                        Console.WriteLine($"[JWT_CHALLENGE] Error={ctx.Error} Desc={ctx.ErrorDescription}");
                        if (prevOnChallenge is not null) await prevOnChallenge(ctx);
                    };
                });
            });
        }
    }
}