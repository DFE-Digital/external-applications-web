using System.Diagnostics.CodeAnalysis;
using DfE.CoreLibs.Security.Authorization;
using DfE.CoreLibs.Security.Interfaces;
using DfE.CoreLibs.Security.OpenIdConnect;
using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Infrastructure.Parsers;
using DfE.ExternalApplications.Infrastructure.Providers;
using DfE.ExternalApplications.Infrastructure.Stores;
using DfE.ExternalApplications.Web.Middleware;
using DfE.ExternalApplications.Web.Security;
using DfE.ExternalApplications.Web.Services;
using GovUK.Dfe.ExternalApplications.Api.Client;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using GovUK.Dfe.ExternalApplications.Api.Client.Extensions;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using System.Diagnostics.CodeAnalysis;

var builder = WebApplication.CreateBuilder(args);

ConfigurationManager configuration = builder.Configuration;

// Add services to the container.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/", "OpenIdConnectPolicy");
    options.Conventions.AllowAnonymousToPage("/Index");

});

builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();
builder.Services.AddMemoryCache();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie()
.AddCustomOpenIdConnect(configuration, sectionName: "DfESignIn");

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

builder.Services.AddExternalApplicationsApiClient<ITokensClient, TokensClient>(configuration);
builder.Services.AddExternalApplicationsApiClient<IUsersClient, UsersClient>(configuration);
builder.Services.AddExternalApplicationsApiClient<IApplicationsClient, ApplicationsClient>(configuration);
builder.Services.AddExternalApplicationsApiClient<ITemplatesClient, TemplatesClient>(configuration);

builder.Services.AddGovUkFrontend();
builder.Services.AddSingleton<IActionContextAccessor, ActionContextAccessor>();
builder.Services.AddScoped<IHtmlHelper, HtmlHelper>();
builder.Services.AddScoped<IFieldRendererService, FieldRendererService>();
builder.Services.AddSingleton<ITemplateStore, ApiTemplateStore>();

//var templatesPath = Path.Combine(builder.Environment.ContentRootPath, "templates");
//builder.Services.AddSingleton<ITemplateStore>(
//    new JsonFileTemplateStore(templatesPath));

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

await app.RunAsync();


[ExcludeFromCodeCoverage]
public static partial class Program { }
