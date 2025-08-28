using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using DfE.CoreLibs.Http.Models;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Security;

[ExcludeFromCodeCoverage]
public class TokenExchangeHandler(
    IHttpContextAccessor httpContextAccessor,
    IInternalUserTokenStore tokenStore,
    ITokensClient tokensClient,
    ITokenAcquisitionService tokenAcquisitionService,
    ITokenStateManager tokenStateManager,
    ILogger<TokenExchangeHandler> logger)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(">>>>>>>>>> TokenExchange >>> ENTRY: Processing request: {Method} {Uri} - Time: {Time}", 
            request.Method, request.RequestUri, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff UTC"));

        var httpContext = httpContextAccessor.HttpContext;
        
        if (httpContext == null)
        {
            logger.LogError(">>>>>>>>>> TokenExchange >>> HttpContext is null for request: {Method} {Uri}", 
                request.Method, request.RequestUri);
            return UnauthorizedResponse(request);
        }

        // Check if user is authenticated
        if (httpContext.User?.Identity?.IsAuthenticated != true)
        {
            logger.LogWarning(">>>>>>>>>> TokenExchange >>> User not authenticated for request: {Method} {Uri}", 
                request.Method, request.RequestUri);
            return UnauthorizedResponse(request);
        }

        var userName = httpContext.User.Identity.Name ?? "Unknown";
        logger.LogInformation(">>>>>>>>>> TokenExchange >>> Processing request for user: {UserName}", userName);

        try
        {
            // Get current token state
            var tokenState = await tokenStateManager.GetCurrentTokenStateAsync();

            // Check if we should force logout
            if (tokenStateManager.ShouldForceLogout(tokenState))
            {
                logger.LogWarning(">>>>>>>>>> TokenExchange >>> Token state requires logout for user: {UserName}, Reason: {Reason}", 
                    userName, tokenState.LogoutReason);

                await tokenStateManager.ForceCompleteLogoutAsync();
                return UnauthorizedResponse(request);
            }

            // Check if we have a valid OBO token
            if (tokenState.OboToken.IsValid)
            {
                logger.LogDebug(">>>>>>>>>> TokenExchange >>> Valid OBO token found, adding to request");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenState.OboToken.Value);
                return await base.SendAsync(request, cancellationToken);
            }

            // Attempt token refresh if possible
            if (tokenState.CanRefresh && tokenState.IsAnyTokenExpired)
            {
                logger.LogInformation(">>>>>>>>>> TokenExchange >>> Attempting token refresh for user: {UserName}", userName);
                
                var refreshed = await tokenStateManager.RefreshTokensIfPossibleAsync();
                if (refreshed)
                {
                    logger.LogInformation(">>>>>>>>>> TokenExchange >>> Token refresh successful, retrying token state");
                    tokenState = await tokenStateManager.GetCurrentTokenStateAsync();
                }
            }

            // Try to exchange for OBO token
            if (tokenState.ExternalIdpToken.IsValid && !string.IsNullOrEmpty(tokenState.ExternalIdpToken.Value))
            {
                logger.LogInformation(">>>>>>>>>> TokenExchange >>> Attempting token exchange for user: {UserName}", userName);
                
                var oboToken = await ExchangeTokenAsync(tokenState.ExternalIdpToken.Value);
                if (!string.IsNullOrEmpty(oboToken))
                {
                    logger.LogInformation(">>>>>>>>>> TokenExchange >>> Token exchange successful for user: {UserName}", userName);
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", oboToken);
                    return await base.SendAsync(request, cancellationToken);
                }
                else
                {
                    logger.LogWarning(">>>>>>>>>> TokenExchange >>> Token exchange failed for user: {UserName}", userName);
                    await tokenStateManager.ForceCompleteLogoutAsync();
                    return UnauthorizedResponse(request);
                }
            }
            else
            {
                logger.LogWarning(">>>>>>>>>> TokenExchange >>> No valid External IDP token available for user: {UserName}", userName);
                await tokenStateManager.ForceCompleteLogoutAsync();
                return UnauthorizedResponse(request);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> TokenExchange >>> Error during token exchange for user: {UserName}", userName);
            return UnauthorizedResponse(request);
        }
    }

    private async Task<string?> ExchangeTokenAsync(string externalIdpToken)
    {
        try
        {
            logger.LogInformation(">>>>>>>>>> TokenExchange >>> Starting token exchange process");

            // Get Azure token for authentication with the token exchange endpoint
            var azureToken = await tokenAcquisitionService.GetTokenAsync();
            if (string.IsNullOrEmpty(azureToken))
            {
                logger.LogError(">>>>>>>>>> TokenExchange >>> Failed to get Azure token for exchange endpoint authentication");
                return null;
            }

            logger.LogDebug(">>>>>>>>>> TokenExchange >>> Azure token acquired for exchange endpoint");

            // Create exchange request
            var exchangeRequest = new ExchangeTokenRequest(externalIdpToken);

            logger.LogDebug(">>>>>>>>>> TokenExchange >>> Calling token exchange endpoint");

            // Call exchange endpoint
            var result = await tokensClient.ExchangeAsync(exchangeRequest);

            if (!string.IsNullOrEmpty(result?.AccessToken))
            {
                logger.LogInformation(">>>>>>>>>> TokenExchange >>> Token exchange successful, storing OBO token");
                
                // Store the new OBO token
                tokenStore.SetToken(result.AccessToken);
                
                return result.AccessToken;
            }
            else
            {
                logger.LogWarning(">>>>>>>>>> TokenExchange >>> Token exchange failed - no access token returned");
                return null;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> TokenExchange >>> Exception during token exchange");
            return null;
        }
    }

    private HttpResponseMessage UnauthorizedResponse(HttpRequestMessage request)
    {
        logger.LogWarning(">>>>>>>>>> TokenExchange >>> Returning 401 Unauthorized for request: {Method} {Uri}", 
            request.Method, request.RequestUri);

        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        
        var errorResponse = new ExceptionResponse
        {
            ExceptionType = "Unauthorized",
            Message = "Authentication tokens are invalid or expired",
            Timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(errorResponse);
        response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        
        return response;
    }
}