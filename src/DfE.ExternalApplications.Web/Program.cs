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
using DfE.ExternalApplications.Web.Middleware;
using DfE.ExternalApplications.Web.Security;
using DfE.ExternalApplications.Web.Services;
using GovUk.Frontend.AspNetCore;
using GovUK.Dfe.ExternalApplications.Api.Client;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using GovUK.Dfe.ExternalApplications.Api.Client.Extensions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using DfE.CoreLibs.Security;
using DfE.CoreLibs.Security.Configurations;
using System.Diagnostics.CodeAnalysis;


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
builder.Services.AddControllers();

builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();
builder.Services.AddMemoryCache();

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

builder.Services.AddExternalApplicationsApiClient<ITokensClient, TokensClient>(configuration);
builder.Services.AddExternalApplicationsApiClient<IUsersClient, UsersClient>(configuration);
builder.Services.AddExternalApplicationsApiClient<IApplicationsClient, ApplicationsClient>(configuration);
builder.Services.AddExternalApplicationsApiClient<ITemplatesClient, TemplatesClient>(configuration);

builder.Services.AddGovUkFrontend(options => options.Rebrand = true);
builder.Services.AddSingleton<IActionContextAccessor, ActionContextAccessor>();
builder.Services.AddScoped<IHtmlHelper, HtmlHelper>();
builder.Services.AddScoped<IFieldRendererService, FieldRendererService>();
builder.Services.AddScoped<IApplicationResponseService, ApplicationResponseService>();

// New refactored services for Clean Architecture
builder.Services.AddScoped<IFieldFormattingService, FieldFormattingService>();
builder.Services.AddScoped<ITemplateManagementService, TemplateManagementService>();
builder.Services.AddScoped<IApplicationStateService, ApplicationStateService>();
builder.Services.AddScoped<IFileUploadService, FileUploadService>();

builder.Services.AddScoped<IAutocompleteService, AutocompleteService>();
builder.Services.AddScoped<IApiErrorParser, ApiErrorParser>();
builder.Services.AddScoped<IModelStateErrorHandler, ModelStateErrorHandler>();
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
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseHostTemplateResolution();

app.UseAuthentication();
app.UsePermissionsCache();
app.UseTokenExpiryCheck();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

app.UseGovUkFrontend();

await app.RunAsync();


[ExcludeFromCodeCoverage]
public static partial class Program { }
