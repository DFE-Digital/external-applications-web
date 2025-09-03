﻿using System;
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
        var httpContext = httpContextAccessor.HttpContext;
        
        if (httpContext == null)
        {
            return UnauthorizedResponse(request);
        }

        // Check if user is authenticated
        if (httpContext.User?.Identity?.IsAuthenticated != true)
        {
            return UnauthorizedResponse(request);
        }

        var userName = httpContext.User.Identity.Name ?? "Unknown";

        try
        {
            // Get current token state
            var tokenState = await tokenStateManager.GetCurrentTokenStateAsync();

            // Check if we should force logout
            if (tokenStateManager.ShouldForceLogout(tokenState))
            {
                await tokenStateManager.ForceCompleteLogoutAsync();
                return UnauthorizedResponse(request);
            }

            // Check if we have a valid OBO token
            if (tokenState.OboToken.IsValid)
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenState.OboToken.Value);
                return await base.SendAsync(request, cancellationToken);
            }

            // Attempt token refresh if possible
            if (tokenState.CanRefresh)
            {
                var refreshed = await tokenStateManager.RefreshTokensIfPossibleAsync();
                if (refreshed)
                {
                    tokenState = await tokenStateManager.GetCurrentTokenStateAsync();
                }
            }

            // Try to exchange for OBO token
            if (tokenState.ExternalIdpToken.IsValid && !string.IsNullOrEmpty(tokenState.ExternalIdpToken.Value))
            {
                var oboToken = await ExchangeTokenAsync(tokenState.ExternalIdpToken.Value);
                if (!string.IsNullOrEmpty(oboToken))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", oboToken);
                    return await base.SendAsync(request, cancellationToken);
                }
                else
                {
                    await tokenStateManager.ForceCompleteLogoutAsync();
                    return UnauthorizedResponse(request);
                }
            }
            else
            {
                await tokenStateManager.ForceCompleteLogoutAsync();
                return UnauthorizedResponse(request);
            }
        }
        catch (Exception ex)
        {
            return UnauthorizedResponse(request);
        }
    }

    private async Task<string?> ExchangeTokenAsync(string externalIdpToken)
    {
        try
        {
            // Get Azure token for authentication with the token exchange endpoint
            var azureToken = await tokenAcquisitionService.GetTokenAsync();
            if (string.IsNullOrEmpty(azureToken))
            {
                return null;
            }

            // Create exchange request
            var exchangeRequest = new ExchangeTokenRequest(externalIdpToken);

            // Call exchange endpoint
            var result = await tokensClient.ExchangeAsync(exchangeRequest);

            if (!string.IsNullOrEmpty(result?.AccessToken))
            {
                // Store the new OBO token
                tokenStore.SetToken(result.AccessToken);
                
                return result.AccessToken;
            }
            else
            {
                return null;
            }
        }
        catch (Exception ex)
        {
            return null;
        }
    }

    private HttpResponseMessage UnauthorizedResponse(HttpRequestMessage request)
    {
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