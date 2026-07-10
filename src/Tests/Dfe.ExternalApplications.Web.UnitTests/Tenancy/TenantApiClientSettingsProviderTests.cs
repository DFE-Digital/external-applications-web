using DfE.ExternalApplications.Web.Tenancy;
using Microsoft.Extensions.Configuration;

namespace Dfe.ExternalApplications.Web.UnitTests.Tenancy;

public class TenantApiClientSettingsProviderTests
{
    [Fact]
    public void GetSettings_ShouldBindFromTenantConfigurationAndSetTenantId()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var tenantConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExternalApplicationsApiClient:BaseUrl"] = "https://api.example/",
                ["ExternalApplicationsApiClient:ClientId"] = "client-id",
                ["ExternalApplicationsApiClient:Scope"] = "api://scope/.default"
            })
            .Build();

        var tenantContext = new TenantRequestContext
        {
            TenantId = tenantId,
            TenantConfiguration = tenantConfiguration
        };

        var hostConfiguration = new ConfigurationBuilder().Build();
        var provider = new TenantApiClientSettingsProvider(tenantContext, hostConfiguration);

        var settings = provider.GetSettings();

        Assert.Equal("https://api.example/", settings.BaseUrl);
        Assert.Equal("client-id", settings.ClientId);
        Assert.Equal(tenantId, settings.TenantId);
    }

    [Fact]
    public void GetSettings_ShouldThrow_WhenTenantConfigurationMissing()
    {
        var provider = new TenantApiClientSettingsProvider(
            new TenantRequestContext(),
            new ConfigurationBuilder().Build());

        Assert.Throws<InvalidOperationException>(() => provider.GetSettings());
    }
}
