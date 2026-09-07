using DfE.ExternalApplications.Web.Authentication;
using DfE.ExternalApplications.Web.Security;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.CoreLibs.Security.EntraSso;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dfe.ExternalApplications.Web.UnitTests.Security;

public class DynamicAuthenticationSchemeProviderTests
{
    [Fact]
    public async Task GetDefaultForbidSchemeAsync_UsesCookie_NotOpenIdConnect()
    {
        var provider = CreateProvider();

        var scheme = await provider.GetDefaultForbidSchemeAsync();

        Assert.NotNull(scheme);
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, scheme!.Name);
    }

    [Fact]
    public async Task GetDefaultChallengeSchemeAsync_UsesOpenIdConnect()
    {
        var provider = CreateProvider();

        var scheme = await provider.GetDefaultChallengeSchemeAsync();

        Assert.NotNull(scheme);
        Assert.Equal(OpenIdConnectDefaults.AuthenticationScheme, scheme!.Name);
    }

    [Fact]
    public async Task GetDefaultAuthenticateSchemeAsync_UsesCookie()
    {
        var provider = CreateProvider();

        var scheme = await provider.GetDefaultAuthenticateSchemeAsync();

        Assert.NotNull(scheme);
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, scheme!.Name);
    }

    private static DynamicAuthenticationSchemeProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication()
            .AddCookie()
            .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, _ => { })
            .AddScheme<TestAuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName, _ => { })
            .AddScheme<InternalServiceAuthenticationSchemeOptions, InternalServiceAuthenticationHandler>(
                InternalServiceAuthenticationHandler.SchemeName, _ => { });

        var httpContext = new DefaultHttpContext();
        var sp = services.BuildServiceProvider();
        httpContext.RequestServices = sp;

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var authOptions = sp.GetRequiredService<IOptions<AuthenticationOptions>>();

        return new DynamicAuthenticationSchemeProvider(
            authOptions,
            accessor,
            Options.Create(new TestAuthenticationOptions { Enabled = false }),
            Options.Create(new EntraSsoOptions { Enabled = false }),
            new ConfigurationBuilder().Build());
    }
}
