using System.Threading;
using System.Threading.Tasks;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Request;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using GovUK.Dfe.ExternalApplications.Api.Client.Security;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Extensions;

public static class TokensClientExtensions
{
    public static async Task<ExchangeTokenDto> ExchangeAndStoreAsync(
        this ITokensClient client,
        string idpToken,
        IInternalUserTokenStore tokenStore,
        CancellationToken cancellationToken = default)
    {
        var request = new ExchangeTokenRequest(idpToken);
        var response = await client.ExchangeAsync(request, cancellationToken);
        if (!string.IsNullOrEmpty(response.AccessToken))
        {
            tokenStore.SetToken(response.AccessToken!);
        }
        return response;
    }
}