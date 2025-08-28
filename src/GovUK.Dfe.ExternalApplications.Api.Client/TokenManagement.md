# Enhanced Token Management

The External Applications API Client now includes comprehensive token management to handle token expiry scenarios robustly.

## Overview

The client manages three types of tokens:

1. **Azure Client Credentials Token**: Automatically refreshed by MSAL
2. **External IDP Token (DSI)**: The `id_token` from the external identity provider - expires in 1 hour
3. **OBO Token**: The internal API token obtained via exchange - expires in 1 hour

## Key Features

### 1. Unified Token Expiry Management
- **5-minute safety buffer**: All tokens are considered expired if they expire within 5 minutes
- **Comprehensive monitoring**: Tracks all token types and their expiry times
- **Proactive validation**: Checks tokens before API calls to prevent failures

### 2. Automatic Token Cleanup
- **Force logout**: When any token expires, all tokens are cleared
- **Cache management**: Both in-memory and distributed cache are cleared
- **Consistent state**: Prevents partial token states that cause random 401/403 errors

### 3. Intelligent Token Exchange
- **Smart caching**: Only exchanges tokens when necessary
- **Validation first**: Checks all token validity before attempting exchanges
- **Comprehensive logging**: Detailed logging for troubleshooting

## Services

### ITokenExpiryService
Central service for token expiry management:

```csharp
public interface ITokenExpiryService
{
    bool IsAnyTokenExpired();
    DateTime? GetEarliestTokenExpiry();
    TokenExpiryInfo GetTokenExpiryInfo();
    void ForceLogout();
}
```

### TokenExpiryMiddleware
Proactive middleware that checks token expiry on every request:
- Returns JSON error responses for AJAX/API requests
- Sets flags for web requests to handle logout appropriately

### Enhanced IInternalUserTokenStore
Extended interface with additional validation methods:

```csharp
public interface IInternalUserTokenStore
{
    string? GetToken();
    void SetToken(string token);
    void ClearToken();
    bool IsTokenValid();
    DateTime? GetTokenExpiry();
}
```

## Usage in Razor Pages Applications

### 1. Service Registration

```csharp
// In Program.cs or Startup.cs
var apiSettings = new ApiClientSettings
{
    BaseUrl = "https://your-api.com",
    ClientId = "your-client-id",
    ClientSecret = "your-client-secret",
    Authority = "https://login.microsoftonline.com/your-tenant",
    Scope = "your-scope"
};

services.AddExternalApplicationsApiClient(apiSettings);
```

### 2. Middleware Setup

```csharp
// In Program.cs - add after authentication but before authorization
app.UseAuthentication();
app.UseTokenExpiryMiddleware(); // Add this line
app.UseAuthorization();

// Optional: Handle token expiry for web requests
app.UseTokenExpiryHandler((context, expiryInfo) =>
{
    // Custom logic for handling expired tokens
    context.Response.Redirect("/Account/Login?expired=true");
});
```

### 3. Controller/Page Usage

```csharp
public class HomeController : Controller
{
    private readonly ITokenExpiryService _tokenExpiryService;

    public HomeController(ITokenExpiryService tokenExpiryService)
    {
        _tokenExpiryService = tokenExpiryService;
    }

    public IActionResult Index()
    {
        // Check if logout is required (prevents infinite loops)
        if (_tokenExpiryService.IsLogoutRequired())
        {
            return RedirectToAction("Logout", "Account");
        }

        // Check token expiry status
        var expiryInfo = _tokenExpiryService.GetTokenExpiryInfo();
        if (expiryInfo.IsAnyTokenExpired && !expiryInfo.CanProceedWithExchange)
        {
            return RedirectToAction("Login", "Account");
        }

        // Get detailed expiry information
        ViewBag.TokenInfo = expiryInfo;

        return View();
    }
}
```

### 4. Extension Methods

```csharp
// In any class with access to IServiceProvider
public void CheckTokens(IServiceProvider serviceProvider)
{
    // Check if logout is required first (prevents loops)
    if (serviceProvider.IsLogoutRequired())
    {
        // Handle logout - redirect to sign out
        return;
    }

    // Quick check for expired tokens
    if (serviceProvider.AreTokensExpired())
    {
        // Handle expired tokens
    }

    // Get detailed info
    var info = serviceProvider.GetTokenExpiryInfo();
    
    // Force logout if needed
    serviceProvider.ForceLogout();
    
    // Force token refresh
    serviceProvider.ForceTokenRefresh();
}
```

## Token Expiry Logic

The system distinguishes between different token scenarios:

### Token States:
1. **Valid**: Token exists and expires more than 5 minutes from now
2. **Expired**: Token exists but expires within 5 minutes (or already expired)
3. **Missing**: Token doesn't exist (first-time access scenario)

### Decision Logic:
- **Can Proceed with Exchange**: Either External IDP token is valid OR OBO token exists and is valid
- **Force Logout**: External IDP token is expired/missing AND cannot proceed with exchange
- **First-Time Access**: OBO token is missing but External IDP token is valid (proceeds to token exchange)

### When tokens are actually expired:
1. All cached tokens are cleared (both HttpContext.Items and distributed cache)
2. For API/AJAX requests: Returns 401 with JSON error response
3. For web requests: Sets `TokenExpired` flag in HttpContext.Items
4. Consuming application should handle logout/redirect as appropriate

### First-Time Access Handling:
- When a user logs in for the first time, they have a valid External IDP token but no OBO token
- The system detects this as "first-time access" and proceeds with token exchange
- No logout/redirect is triggered, preventing infinite redirect loops

### Logout Loop Prevention:
- **Multi-layer protection** against infinite logout loops
- **Per-request protection**: When `ForceLogout()` is called, sets `RequireLogout` flag in `HttpContext.Items`
- **Cross-request protection**: Sets a distributed cache flag for the user that persists for 10 minutes
- **Authentication state check**: If user is not authenticated, no token checking is performed
- **Re-authentication recovery**: If user re-authenticates while logout flag exists:
  - Flag is automatically cleared
  - All cached tokens are cleared to force fresh acquisition
  - Returns fresh state with `CanProceedWithExchange = true` to allow new token exchange
- Subsequent calls to `GetTokenExpiryInfo()` detect these conditions and return safe state
- The application should check `IsLogoutRequired()` and handle the actual sign-out process

## Logging

All token operations are logged with the prefix `>>>>>>>>>> TokenExpiry >>>` or `>>>>>>>>>> Authentication >>>` for easy filtering and monitoring.

Key log events:
- Token expiry checks and results
- Token exchange operations
- Cache operations
- Error conditions

## Benefits

1. **Eliminates random 401/403 errors**: Proactive token validation prevents partial expiry states
2. **Consistent user experience**: Users are logged out cleanly when tokens expire
3. **Better monitoring**: Comprehensive logging for troubleshooting
4. **Simplified integration**: Easy-to-use extension methods and middleware
5. **Robust error handling**: Graceful handling of various token failure scenarios

## Migration from Previous Version

The enhanced token management is backward compatible. Existing code will continue to work, but you can optionally:

1. Add the middleware for proactive checking
2. Use the new `ITokenExpiryService` for better token monitoring
3. Implement custom token expiry handling using the extension methods
