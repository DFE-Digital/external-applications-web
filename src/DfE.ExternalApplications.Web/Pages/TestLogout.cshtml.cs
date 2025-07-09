using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using DfE.CoreLibs.Security.Configurations;

namespace DfE.ExternalApplications.Web.Pages;

[ExcludeFromCodeCoverage]
[AllowAnonymous]
public class TestLogoutModel(
    IOptions<TestAuthenticationOptions> testAuthOptions,
    ILogger<TestLogoutModel> logger)
    : PageModel
{
    private readonly TestAuthenticationOptions _testAuthOptions = testAuthOptions.Value;

    public IActionResult OnGet()
    {
        // Only allow access if test authentication is enabled
        if (!_testAuthOptions.Enabled)
        {
            return NotFound();
        }

        return Page();
    }

    public IActionResult OnPost()
    {
        // Only allow access if test authentication is enabled
        if (!_testAuthOptions.Enabled)
        {
            return NotFound();
        }

        // Clear test authentication session data
        HttpContext.Session.Remove("TestAuth:Email");
        HttpContext.Session.Remove("TestAuth:Token");

        logger.LogInformation("Test authentication session cleared");

        // Redirect to home page
        return Redirect("/");
    }
} 