using DfE.ExternalApplications.Web.Extensions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;

namespace Dfe.ExternalApplications.Web.UnitTests.Extensions;

public class SharedDataProtectionExtensionsTests
{
    [Fact]
    public void AddSharedDataProtection_PersistsKeyRingToRedis_WithStableApplicationName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IConnectionMultiplexer>());

        services.AddSharedDataProtection();

        using var provider = services.BuildServiceProvider();

        var dataProtection = provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        Assert.Equal(
            SharedDataProtectionExtensions.ApplicationName,
            dataProtection.ApplicationDiscriminator);

        var keyManagement = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        Assert.IsType<RedisXmlRepository>(keyManagement.XmlRepository);
    }
}
