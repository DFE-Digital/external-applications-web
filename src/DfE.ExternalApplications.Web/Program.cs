using DfE.CoreLibs.Security;
using DfE.CoreLibs.Security.Authorization;
using DfE.CoreLibs.Security.Configurations;
using DfE.CoreLibs.Security.Interfaces;
using DfE.CoreLibs.Security.OpenIdConnect;
using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Infrastructure.Parsers;
using DfE.ExternalApplications.Infrastructure.Providers;
using DfE.ExternalApplications.Infrastructure.Services;
using DfE.ExternalApplications.Infrastructure.Stores;
using DfE.ExternalApplications.Web.Authentication;
using DfE.ExternalApplications.Web.Filters;
using DfE.ExternalApplications.Web.Middleware;
using DfE.ExternalApplications.Web.Security;
using DfE.ExternalApplications.Web.Services;
using DfE.ExternalApplications.Web.Interfaces;
using DfE.ExternalApplications.Web.Extensions;
using Microsoft.AspNetCore.ResponseCompression;
using GovUk.Frontend.AspNetCore;
using GovUK.Dfe.ExternalApplications.Api.Client;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using GovUK.Dfe.ExternalApplications.Api.Client.Extensions;
using GovUK.Dfe.ExternalApplications.Api.Client.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

ConfigurationManager configuration = builder.Configuration;

// Configure test authentication options
builder.Services.Configure<TestAuthenticationOptions>(
    configuration.GetSection(TestAuthenticationOptions.SectionName));

// Check if test authentication is enabled
var testAuthOptions = configuration.GetSection(TestAuthenticationOptions.SectionName).Get<TestAuthenticationOptions>();
var isTestAuthEnabled = testAuthOptions?.Enabled ?? false;

// Configure token settings for test authentication
if (isTestAuthEnabled && testAuthOptions != null)
{
    builder.Services.Configure<DfE.CoreLibs.Security.Configurations.TokenSettings>(options =>
    {
        options.SecretKey = testAuthOptions.JwtSigningKey;
        options.Issuer = testAuthOptions.JwtIssuer;
        options.Audience = testAuthOptions.JwtAudience;
        options.TokenLifetimeMinutes = 60; // 1 hour default
    });
}

// Add services to the container.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.ConfigureFilter(new ExternalApiPageExceptionFilter());

    options.Conventions.AuthorizeFolder("/", "OpenIdConnectPolicy");
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/Logout");
    
    // Allow anonymous access to test login page when test auth is enabled
    if (isTestAuthEnabled)
    {
        options.Conventions.AllowAnonymousToPage("/TestLogin");
        options.Conventions.AllowAnonymousToPage("/TestLogout");
    }
});

// Add controllers for API endpoints
builder.Services.AddControllers(options =>
{
    options.Filters.Add<ExternalApiMvcExceptionFilter>();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();
builder.Services.AddMemoryCache();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

// Configure authentication based on test mode
if (isTestAuthEnabled)
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = TestAuthenticationHandler.SchemeName;
        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignOutScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddScheme<TestAuthenticationSchemeOptions, TestAuthenticationHandler>(
        TestAuthenticationHandler.SchemeName, 
        options => { });
}
else
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        options.DefaultSignOutScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddCustomOpenIdConnect(configuration, sectionName: "DfESignIn");
}

builder.Services
    .AddApplicationAuthorization(
        configuration,
        policyCustomizations: null,
        apiAuthenticationScheme: null,
        configureResourcePolicies: opts =>
        {
            opts.Actions.AddRange(["Read", "Write"]);
            opts.ClaimType = "permission";
        });

builder.Services.AddScoped<ICustomClaimProvider, PermissionsClaimProvider>();

// Add HttpClient for API calls
builder.Services.AddHttpClient();

builder.Services.AddScoped<IContributorService, ContributorService>();

builder.Services.AddExternalApplicationsApiClients(configuration);

// Register authentication strategies in consuming app (Clean Architecture)
// These were moved out of the library to remove coupling
if (isTestAuthEnabled)
{
    // Register TestAuthenticationStrategy when test auth is enabled
    builder.Services.AddScoped<IAuthenticationSchemeStrategy, TestAuthenticationStrategy>();
}
else
{
    // Register OidcAuthenticationStrategy when OIDC is enabled
    builder.Services.AddScoped<IAuthenticationSchemeStrategy, OidcAuthenticationStrategy>();
}

builder.Services.AddGovUkFrontend(options => options.Rebrand = true);
builder.Services.AddSingleton<IActionContextAccessor, ActionContextAccessor>();
builder.Services.AddScoped<IHtmlHelper, HtmlHelper>();
builder.Services.AddWebLayerServices();
builder.Services.AddScoped<IApplicationResponseService, ApplicationResponseService>();

// New refactored services for Clean Architecture
builder.Services.AddScoped<IFieldFormattingService, FieldFormattingService>();
builder.Services.AddScoped<ITemplateManagementService, TemplateManagementService>();
builder.Services.AddScoped<IApplicationStateService, ApplicationStateService>();
builder.Services.AddScoped<IFileUploadService, FileUploadService>();

// Conditional Logic Services
builder.Services.AddScoped<IConditionalLogicEngine, ConditionalLogicEngine>();
builder.Services.AddScoped<IConditionalLogicOrchestrator, ConditionalLogicOrchestrator>();

builder.Services.AddScoped<IAutocompleteService, AutocompleteService>();
builder.Services.AddScoped<IComplexFieldConfigurationService, ComplexFieldConfigurationService>();
builder.Services.AddScoped<IComplexFieldRendererFactory, ComplexFieldRendererFactory>();
builder.Services.AddScoped<IComplexFieldRenderer, AutocompleteComplexFieldRenderer>();
builder.Services.AddScoped<IComplexFieldRenderer, CompositeComplexFieldRenderer>();
builder.Services.AddScoped<IComplexFieldRenderer, UploadComplexFieldRenderer>();

builder.Services.AddSingleton<ITemplateStore, ApiTemplateStore>();

 

// Add test token handler and services when test authentication is enabled
if (isTestAuthEnabled)
{
    builder.Services.AddUserTokenService(configuration);
    builder.Services.AddScoped<ITestAuthenticationService, TestAuthenticationService>();
}

builder.Services.AddServiceCaching(configuration);


builder.Services.AddSingleton<IFormTemplateParser, JsonFormTemplateParser>();
builder.Services.AddScoped<IFormTemplateProvider, FormTemplateProvider>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    //app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        const int days = 30;
        ctx.Context.Response.Headers["Cache-Control"] = $"public, max-age={days * 24 * 60 * 60}";
    }
});

app.UseRouting();
app.UseResponseCompression();

app.UseSession();
app.UseHostTemplateResolution();

app.UseStatusCodePages(ctx =>
{
    var c = ctx.HttpContext.Response.StatusCode;
    if (c == 401) ctx.HttpContext.Response.Redirect("/Error/Forbidden");
    else if (c == 403) ctx.HttpContext.Response.Redirect("/Error/Forbidden");
    return Task.CompletedTask;
});

app.UseAuthentication();
//app.UseTokenExpiryCheck();
app.UseTokenExpiryMiddleware(); // Add this line

app.UsePermissionsCache();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

app.UseGovUkFrontend();

app.UseTokenExpiryHandler(async (context, expiryInfo) =>
{
    // Check if we've already processed logout for this request to prevent loops
    if (context.Items.ContainsKey("LogoutProcessed"))
    {
        context.Response.Redirect("/");
        return;
    }

    // Mark that we're processing logout
    context.Items["LogoutProcessed"] = true;

    try
    {
        // Handle test authentication if available
        var testAuth = context.RequestServices.GetService<ITestAuthenticationService>();
        if (testAuth is not null)
        {
            await testAuth.SignOutAsync(context);
        }

        // Sign out of cookie authentication
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // Also sign out of OpenId Connect if you're using it
        // Uncomment this if you need to clear the external authentication
        //await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
        //{
        //    RedirectUri = "/" // Specify where to redirect after logout
        //});

        // Clear any additional session data or custom authentication
        context.Session?.Clear(); // If you're using sessions

        // Instead of direct redirect, use a proper logout page or add query parameter
        context.Response.Redirect("/");
    }
    catch (Exception ex)
    {

        // Fallback redirect
        context.Response.Redirect("/?logout=error");
    }
});

await app.RunAsync();


[ExcludeFromCodeCoverage]
public static partial class Program { }
