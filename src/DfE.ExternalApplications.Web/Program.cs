using DfE.CoreLibs.Security.Authorization;
using DfE.CoreLibs.Security.Interfaces;
using DfE.CoreLibs.Security.OpenIdConnect;
using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Infrastructure.Parsers;
using DfE.ExternalApplications.Infrastructure.Providers;
using DfE.ExternalApplications.Infrastructure.Stores;
using DfE.ExternalApplications.Web.Security;
using DfE.ExternalApplications.Web.Services;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

var builder = WebApplication.CreateBuilder(args);

ConfigurationManager configuration = builder.Configuration;

// Add services to the container.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/", "OpenIdConnectPolicy");
});

builder.Services.AddHttpContextAccessor();

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

builder.Services.AddGovUkFrontend();
builder.Services.AddSingleton<IActionContextAccessor, ActionContextAccessor>();
builder.Services.AddScoped<IHtmlHelper, HtmlHelper>();
builder.Services.AddScoped<IFieldRendererService, FieldRendererService>();
builder.Services.AddHttpClient<ITemplateStore, ApiTemplateStore>(client =>
{
    client.BaseAddress = new Uri(configuration["Backend:BaseUrl"]!);
});
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

app.UseAuthorization();

app.MapRazorPages();

await app.RunAsync();
