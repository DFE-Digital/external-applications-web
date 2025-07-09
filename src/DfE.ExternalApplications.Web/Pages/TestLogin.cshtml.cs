using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using DfE.ExternalApplications.Web.Services;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using DfE.CoreLibs.Security.Configurations;

namespace DfE.ExternalApplications.Web.Pages;

[ExcludeFromCodeCoverage]
[AllowAnonymous]
public class TestLoginModel : PageModel
{
    private readonly TestAuthenticationOptions _testAuthOptions;
    private readonly ITestAuthenticationService _testAuthenticationService;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public TestLoginModel(
        IOptions<TestAuthenticationOptions> testAuthOptions,
        ITestAuthenticationService testAuthenticationService)
    {
        _testAuthOptions = testAuthOptions.Value;
        _testAuthenticationService = testAuthenticationService;
    }

    public IActionResult OnGet()
    {
        // Only allow access if test authentication is enabled
        if (!_testAuthOptions.Enabled)
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Only allow access if test authentication is enabled
        if (!_testAuthOptions.Enabled)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _testAuthenticationService.AuthenticateAsync(Input.Email, HttpContext);
        
        if (!result.IsSuccess)
        {
            ErrorMessage = result.ErrorMessage;
            return Page();
        }

        // Redirect to return URL or use the result's redirect URL
        var redirectUrl = ReturnUrl ?? result.RedirectUrl ?? "/Dashboard";
        return Redirect(redirectUrl);
    }

    public class InputModel
    {
        [Required(ErrorMessage = "Email address is required")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address")]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;
    }
} 