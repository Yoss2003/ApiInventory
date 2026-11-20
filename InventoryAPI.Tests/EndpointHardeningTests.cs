using InventoryAPI.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using System.Reflection;

namespace InventoryAPI.Tests;

public sealed class EndpointHardeningTests
{
    [Fact]
    public void PasswordRecoveryCompletion_RequiresAuthenticationRateLimitPolicy()
    {
        var method = typeof(PasswordRecoveryController).GetMethod(nameof(PasswordRecoveryController.Complete));
        Assert.NotNull(method);
        var attribute = method!.GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal("auth", attribute!.PolicyName);
    }

    [Fact]
    public void SensitiveUserOperations_UseAuthenticationRateLimitPolicy()
    {
        AssertRateLimited(nameof(UsersController.Enable2FA));
        AssertRateLimited(nameof(UsersController.TestEmailConfiguration));
    }

    private static void AssertRateLimited(string methodName)
    {
        var method = typeof(UsersController).GetMethod(methodName);
        Assert.NotNull(method);
        var attribute = method!.GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal("auth", attribute!.PolicyName);
    }
}
