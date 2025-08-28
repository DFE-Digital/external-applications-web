using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Security;

/// <summary>
/// Backward-compatible implementation that delegates to the new TokenStateManager
/// Maintains existing public API while using clean architecture internally
/// </summary>
public class TokenExpiryService(
    ITokenStateManager tokenStateManager,
    ILogger<TokenExpiryService> logger) : ITokenExpiryService
{
    public bool IsAnyTokenExpired()
    {
        logger.LogDebug(">>>>>>>>>> TokenExpiry >>> Checking if any token is expired (sync)");
        
        var info = GetTokenExpiryInfoAsync().GetAwaiter().GetResult();
        var result = info.IsAnyTokenExpired;
        
        logger.LogInformation(">>>>>>>>>> TokenExpiry >>> Token expiry check result: {IsExpired}, Reason: {Reason}", 
            result, info.ExpiryReason ?? "All tokens valid");
        
        return result;
    }

    public DateTime? GetEarliestTokenExpiry()
    {
        logger.LogDebug(">>>>>>>>>> TokenExpiry >>> Getting earliest token expiry (sync)");
        
        var info = GetTokenExpiryInfoAsync().GetAwaiter().GetResult();
        
        logger.LogDebug(">>>>>>>>>> TokenExpiry >>> Earliest expiry: {EarliestExpiry}", 
            info.EarliestExpiry?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "None");
        
        return info.EarliestExpiry;
    }

    public TokenExpiryInfo GetTokenExpiryInfo()
    {
        logger.LogDebug(">>>>>>>>>> TokenExpiry >>> Getting token expiry info (sync)");
        return GetTokenExpiryInfoAsync().GetAwaiter().GetResult();
    }

    public async Task<TokenExpiryInfo> GetTokenExpiryInfoAsync()
    {
        logger.LogDebug(">>>>>>>>>> TokenExpiry >>> Getting token expiry info (async)");
        
        try
        {
            var tokenState = await tokenStateManager.GetCurrentTokenStateAsync();
            
            var info = new TokenExpiryInfo
            {
                ExternalIdpTokenExpiry = tokenState.ExternalIdpToken.ExpiryTime,
                OboTokenExpiry = tokenState.OboToken.ExpiryTime,
                EarliestExpiry = tokenState.EarliestExpiry,
                TimeUntilEarliestExpiry = tokenState.EarliestExpiry?.Subtract(DateTime.UtcNow),
                IsAnyTokenExpired = tokenState.IsAnyTokenExpired,
                IsExternalIdpTokenExpired = tokenState.ExternalIdpToken.IsExpired,
                IsOboTokenExpired = tokenState.OboToken.IsExpired,
                IsOboTokenMissing = !tokenState.OboToken.IsPresent,
                CanProceedWithExchange = !tokenStateManager.ShouldForceLogout(tokenState),
                ExpiryReason = tokenState.LogoutReason
            };

            logger.LogInformation(">>>>>>>>>> TokenExpiry >>> Token Expiry Info - " +
                "ExternalIDP: Expired={ExternalExpired}, OBO: Expired={OboExpired}, Missing={OboMissing}, " +
                "AnyExpired={AnyExpired}, CanProceed={CanProceed}, Reason={Reason}",
                info.IsExternalIdpTokenExpired, info.IsOboTokenExpired, info.IsOboTokenMissing,
                info.IsAnyTokenExpired, info.CanProceedWithExchange, info.ExpiryReason);

            return info;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> TokenExpiry >>> Error getting token expiry info");
            
            return new TokenExpiryInfo
            {
                IsAnyTokenExpired = true,
                ExpiryReason = $"Error retrieving token info: {ex.Message}"
            };
        }
    }

    public void ForceLogout()
    {
        logger.LogWarning(">>>>>>>>>> TokenExpiry >>> Force logout called (sync)");
        ForceLogoutAsync().GetAwaiter().GetResult();
    }

    public async Task ForceLogoutAsync()
    {
        logger.LogWarning(">>>>>>>>>> TokenExpiry >>> Force logout called (async)");
        
        try
        {
            var result = await tokenStateManager.ForceCompleteLogoutAsync();
            logger.LogInformation(">>>>>>>>>> TokenExpiry >>> Force logout completed: {Success}", result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> TokenExpiry >>> Error during force logout");
            throw;
        }
    }

    public bool IsLogoutRequired()
    {
        var result = tokenStateManager.IsLogoutRequired();
        logger.LogDebug(">>>>>>>>>> TokenExpiry >>> Is logout required: {IsRequired}", result);
        return result;
    }
}