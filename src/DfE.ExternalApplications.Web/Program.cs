using GovUK.Dfe.CoreLibs.Security;
using GovUK.Dfe.CoreLibs.Security.Authorization;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.CoreLibs.Security.OpenIdConnect;
using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Application.Options;
using DfE.ExternalApplications.Infrastructure.Parsers;
using DfE.ExternalApplications.Infrastructure.Providers;
using DfE.ExternalApplications.Infrastructure.Services;
using DfE.ExternalApplications.Infrastructure.Stores;
using DfE.ExternalApplications.Web.Authentication;
using DfE.ExternalApplications.Web.Extensions;
using DfE.ExternalApplications.Web.Filters;
using DfE.ExternalApplications.Web.Middleware;
using DfE.ExternalApplications.Web.Security;
using DfE.ExternalApplications.Web.Services;
using GovUk.Frontend.AspNetCore;
using GovUK.Dfe.ExternalApplications.Api.Client.Extensions;
using GovUK.Dfe.ExternalApplications.Api.Client.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.ResponseCompression;
using System.Diagnostics.CodeAnalysis;
using GovUK.Dfe.CoreLibs.Security.TokenRefresh.Extensions;
using System.IO.Compression;
using DfE.ExternalApplications.Infrastructure.Consumers;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Entities.Topics;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using GovUK.Dfe.CoreLibs.Messaging.MassTransit.Extensions;
using Microsoft.AspNetCore.Authentication;
using MassTransit;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Exceptions;
using Microsoft.AspNetCore.Http;
using DfE.ExternalApplications.Web.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// Load application-specific configuration from configurations/{APPLICATION_NAME}/ folder
var applicationName = Environment.GetEnvironmentVariable("APPLICATION_NAME") ?? "Transfers";
var environment = builder.Environment.EnvironmentName;

// Determine the configurations folder path
// The configurations folder is inside the Web project: src/DfE.ExternalApplications.Web/configurations/{APPLICATION_NAME}
var configurationsPath = Path.Combine(builder.Environment.ContentRootPath, "configurations", applicationName);

var baseAppsettingsPath = Path.Combine(configurationsPath, "appsettings.json");
var envAppsettingsPath = Path.Combine(configurationsPath, $"appsettings.{environment}.json");

if (Directory.Exists(configurationsPath) && File.Exists(baseAppsettingsPath))
{
    // Clear existing JSON configuration sources and use folder-based configs
    // This ensures folder-based configs REPLACE default appsettings, not merge with them
    var sourcesToRemove = builder.Configuration.Sources
        .Where(s => s.GetType().Name.Contains("Json"))
        .ToList();
    
    foreach (var source in sourcesToRemove)
    {
        builder.Configuration.Sources.Remove(source);
    }
    
    // Add folder-based configuration
    builder.Configuration
        .AddJsonFile(baseAppsettingsPath, optional: false, reloadOnChange: true)
        .AddJsonFile(envAppsettingsPath, optional: true, reloadOnChange: true);
    
    Console.WriteLine($"[Configuration] Application: {applicationName}");
    Console.WriteLine($"[Configuration] Environment: {environment}");
    Console.WriteLine($"[Configuration] Path: {configurationsPath}");
}
else
{
    Console.WriteLine($"[Configuration] WARNING: Folder-based configuration not found at {configurationsPath}");
    Console.WriteLine($"[Configuration] Using default appsettings from project directory.");
}

// Load application-specific user secrets in Development environment
// User secrets are stored in the standard location using the project's UserSecretsId
// The secrets.json file should have sections per application: { "Transfers": {...}, "Lsrp": {...} }
// Right-click project -> "Manage User Secrets" in Visual Studio to edit
if (builder.Environment.IsDevelopment())
{
    // Add standard user secrets (uses UserSecretsId from .csproj)
    builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
    
    // Check if there's an application-specific section in user secrets
    var appSecretsSection = builder.Configuration.GetSection(applicationName);
    if (appSecretsSection.Exists())
    {
        // Bind the application-specific section to the root configuration
        var appSecrets = appSecretsSection.GetChildren();
        foreach (var secret in appSecrets)
        {
            // Add each setting from the app section to the root configuration
            builder.Configuration[secret.Key] = secret.Value;
            
            // Handle nested sections (e.g., "DfESignIn:ClientSecret")
            foreach (var child in secret.GetChildren())
            {
                BindNestedConfiguration(builder.Configuration, secret.Key, child);
            }
        }
        Console.WriteLine($"[Configuration] User secrets loaded for application: {applicationName}");
    }
    else
    {
        Console.WriteLine($"[Configuration] No application-specific secrets found for: {applicationName}");
        Console.WriteLine($"[Configuration] Add a \"{applicationName}\" section to your secrets.json");
        Console.WriteLine($"[Configuration] Right-click project -> 'Manage User Secrets' in Visual Studio");
    }
}

// Helper method to bind nested configuration sections
static void BindNestedConfiguration(ConfigurationManager config, string parentKey, IConfigurationSection section)
{
    var fullKey = $"{parentKey}:{section.Key}";
    if (section.Value != null)
    {
        config[fullKey] = section.Value;
    }
    foreach (var child in section.GetChildren())
    {
        BindNestedConfiguration(config, fullKey, child);
    }
}

// Environment variables always override JSON configuration
builder.Configuration.AddEnvironmentVariables();

ConfigurationManager configuration = builder.Configuration;

builder.Services.AddApplicationInsightsTelemetry(configuration);

// Filter out health check endpoints from Application Insights telemetry
builder.Services.AddApplicationInsightsTelemetryProcessor<HealthCheckTelemetryFilter>();
// Configure test authentication options
builder.Services.Configure<TestAuthenticationOptions>(
    configuration.GetSection(TestAuthenticationOptions.SectionName));

// Check if test authentication is enabled
var testAuthOptions = configuration.GetSection(TestAuthenticationOptions.SectionName).Get<TestAuthenticationOptions>();
var isTestAuthEnabled = testAuthOptions?.Enabled ?? false;

// Configure token settings for test authentication
// This is needed when test auth is enabled
if ((isTestAuthEnabled) && testAuthOptions != null)
{
    builder.Services.Configure<GovUK.Dfe.CoreLibs.Security.Configurations.TokenSettings>(options =>
    {
        options.SecretKey = testAuthOptions.JwtSigningKey;
        options.Issuer = testAuthOptions.JwtIssuer;
        options.Audience = testAuthOptions.JwtAudience;
        options.TokenLifetimeMinutes = 60; // 1 hour default
    });
}

builder.Services.AddUserTokenServiceFactory(
    builder.Configuration,
    new Dictionary<string, string>
    {
        { "InternalService", "InternalServiceAuth" },
    });

// Add services to the container.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    // Increase form value length limit to handle large JSON data in hidden fields
    options.ValueLengthLimit = 1048576; // 1MB limit for form values
    options.ValueCountLimit = 1000; // Allow more form values
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.ConfigureFilter(new ExternalApiPageExceptionFilter());

    options.Conventions.AuthorizeFolder("/", "OpenIdConnectPolicy");
    options.Conventions.AllowAnonymousToPage("/Logout");

    options.Conventions.AuthorizePage("/Diagnostics");
    
    // Allow anonymous access to feedback pages
    options.Conventions.AllowAnonymousToPage("/Feedback/Index");
    options.Conventions.AllowAnonymousToPage("/Feedback/BugReport");
    options.Conventions.AllowAnonymousToPage("/Feedback/Support");
    options.Conventions.AllowAnonymousToPage("/Feedback/General");
    options.Conventions.AllowAnonymousToPage("/Feedback/ThankYou");
    
    options.Conventions.AllowAnonymousToPage("/Shared/Cookies");

    // Allow anonymous access to error pages
    options.Conventions.AllowAnonymousToPage("/Error/NotFound");
    options.Conventions.AllowAnonymousToPage("/Error/Forbidden");
    options.Conventions.AllowAnonymousToPage("/Error/General");
    options.Conventions.AllowAnonymousToPage("/Error/ServerError");
    
    // Allow anonymous access to test pages in non-production environments
    if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test") || builder.Environment.IsStaging())
    {
        options.Conventions.AllowAnonymousToPage("/TestError");
    }
    
    // Allow anonymous access to test login page when test auth is enabled
    if (isTestAuthEnabled)
    {
        options.Conventions.AllowAnonymousToPage("/TestLogin");
        options.Conventions.AllowAnonymousToPage("/TestLogout");
    }
})
.AddSessionStateTempDataProvider();

// Add controllers for API endpoints
builder.Services.AddControllers(options =>
{
    options.Filters.Add<ExternalApiMvcExceptionFilter>();
    
    // Add confirmation interceptor filter globally
    //options.Filters.Add<DfE.ExternalApplications.Web.Filters.ConfirmationInterceptorFilter>();
});

builder.Services.AddHttpContextAccessor();

// Register Cypress authentication services using CoreLibs pattern
//builder.Services.AddKeyedScoped<ICustomRequestChecker, ExternalAppsCypressRequestChecker>("cypress");
builder.Services.AddKeyedScoped<ICustomRequestChecker, InternalAuthRequestChecker>("internal");

//builder.Services.AddScoped<ICypressAuthenticationService, CypressAuthenticationService>();

// Add confirmation interceptor filter globally for all MVC actions
builder.Services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
{
    options.Filters.Add<DfE.ExternalApplications.Web.Filters.ConfirmationInterceptorFilter>();
});

// Add hybrid caching (Memory + Redis) with automatic session support
builder.Services.AddHybridCaching(builder.Configuration);

// Configure session with timeout settings to prevent hanging/blocking
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IOTimeout = TimeSpan.FromSeconds(5); // Prevent indefinite blocking on session I/O
});

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => true;
    options.MinimumSameSitePolicy = SameSiteMode.None;
    options.Secure = CookieSecurePolicy.Always;
});

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "text/html", "text/css", "application/javascript", "text/javascript" });
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest; // Use faster but less compression
});

builder.Services.Configure<TokenRefreshSettings>(configuration.GetSection("TokenRefresh"));

// Register both schemes once, and use a dynamic scheme provider to pick per-request
builder.Services
    .AddAuthentication()
    .AddCookie()
    .AddCustomOpenIdConnect(configuration, sectionName: "DfESignIn", new OpenIdConnectEvents
    {
        OnRemoteFailure = context =>
        {
            var error = context.Failure?.Message ?? "Unknown error";

            if (error.Contains("message.State", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect("/");
                context.HandleResponse(); // Suppress the exception
                return Task.CompletedTask;
            }

            return Task.CompletedTask;
        },

        OnAuthenticationFailed = context =>
        {
            context.HandleResponse();
            context.Response.Redirect("/error?message=" + Uri.EscapeDataString(context.Exception.Message));
            return Task.CompletedTask;
        }
    })
    .AddScheme<TestAuthenticationSchemeOptions, TestAuthenticationHandler>(
        TestAuthenticationHandler.SchemeName,
        options => { })
    .AddScheme<InternalServiceAuthenticationSchemeOptions, InternalServiceAuthenticationHandler>(
        InternalServiceAuthenticationHandler.SchemeName,
        options => { });

// Use DynamicAuthenticationSchemeProvider to route per request
// Checks for Internal Service Auth (forwarder pattern)
// Then Test Auth, then OIDC
builder.Services.AddSingleton<IAuthenticationSchemeProvider, DynamicAuthenticationSchemeProvider>();

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

builder.Services.AddTokenRefreshWithOidc(configuration, "DfESignIn", "TokenRefresh");

// Add HttpClient for API calls
builder.Services.AddHttpClient();

builder.Services.AddScoped<IContributorService, ContributorService>();

builder.Services.AddExternalApplicationsApiClients(configuration);

// Register authentication strategies and composite selector (per-request)
builder.Services.AddScoped<OidcAuthenticationStrategy>();
builder.Services.AddScoped<TestAuthenticationStrategy>();
builder.Services.AddScoped<InternalAuthenticationStrategy>();
builder.Services.AddScoped<IAuthenticationSchemeStrategy, CompositeAuthenticationSchemeStrategy>();

// Register activity-based token refresh services
builder.Services.AddScoped<IUserActivityTracker, UserActivityTracker>();

builder.Services.AddGovUkFrontend(options => options.Rebrand = true);
builder.Services.AddSingleton<IActionContextAccessor, ActionContextAccessor>();
builder.Services.AddScoped<IHtmlHelper, HtmlHelper>();
builder.Services.AddWebLayerServices();
builder.Services.AddScoped<IApplicationResponseService, ApplicationResponseService>();

// Persist cookie tickets server-side so AuthenticationProperties (tokens) don't bloat the browser cookie
builder.Services.AddSingleton<ITicketStore, DistributedCacheTicketStore>();
builder.Services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, ConfigureCookieTicketStore>();

// New refactored services for Clean Architecture
builder.Services.AddScoped<IFieldFormattingService, FieldFormattingService>();
builder.Services.AddScoped<ITemplateManagementService, TemplateManagementService>();
builder.Services.AddScoped<IApplicationStateService, ApplicationStateService>();
builder.Services.AddScoped<IFileUploadService, FileUploadService>();

// Conditional Logic Services
builder.Services.AddScoped<IConditionalLogicEngine, ConditionalLogicEngine>();
builder.Services.AddScoped<IConditionalLogicOrchestrator, ConditionalLogicOrchestrator>();

// Derived Collection Flow Services
builder.Services.AddScoped<IDerivedCollectionFlowService, DerivedCollectionFlowService>();

builder.Services.AddScoped<IAutocompleteService, AutocompleteService>();
builder.Services.AddScoped<IComplexFieldConfigurationService, ComplexFieldConfigurationService>();
builder.Services.AddScoped<IComplexFieldRendererFactory, ComplexFieldRendererFactory>();
builder.Services.AddScoped<IComplexFieldRenderer, AutocompleteComplexFieldRenderer>();
builder.Services.AddScoped<IComplexFieldRenderer, CompositeComplexFieldRenderer>();
builder.Services.AddScoped<IComplexFieldRenderer, UploadComplexFieldRenderer>();

builder.Services.AddSingleton<ITemplateStore, ApiTemplateStore>(); 
builder.Services.AddUserTokenService(configuration);

// Add test token handler and services when test authentication or Cypress is enabled
if (isTestAuthEnabled)
{
    builder.Services.AddScoped<ITestAuthenticationService, TestAuthenticationService>();
}

// Configure Internal Service Auth settings
builder.Services.Configure<InternalServiceAuthOptions>(
    builder.Configuration.GetSection("InternalServiceAuth"));

// Add internal service authentication service (always available)
builder.Services.AddScoped<IInternalServiceAuthenticationService, InternalServiceAuthenticationService>();

builder.Services.AddServiceCaching(configuration);

builder.Services.AddSingleton<IFormTemplateParser, JsonFormTemplateParser>();
builder.Services.AddScoped<IFormTemplateProvider, FormTemplateProvider>();

// Application submission configuration (mapper key and handlers per application)
builder.Services.Configure<ApplicationSubmissionOptions>(configuration.GetSection("ApplicationSubmission"));

// Event mapping and publishing services
builder.Services.AddSingleton<IEventMappingProvider, EventMappingProvider>();
builder.Services.AddKeyedScoped<IEventDataMapper, EventDataMapper>("Default");
builder.Services.AddScoped<IEventDataMapperFactory, EventDataMapperFactory>();
builder.Services.AddSingleton<IEventTypeRegistry, EventTypeRegistry>();

// Application submission handlers (resolved by key from ApplicationSubmission:Handlers)
builder.Services.AddKeyedScoped<IApplicationSubmittedHandler, PublishEventApplicationSubmittedHandler>("PublishEvent");
builder.Services.AddKeyedScoped<IApplicationSubmittedHandler, NoOpApplicationSubmittedHandler>("NoOp");
builder.Services.AddScoped<IApplicationSubmissionOrchestrator, ApplicationSubmissionOrchestrator>();

builder.Services.AddDfEMassTransit(
    configuration,
    configureConsumers: x =>
    {
        x.AddConsumer<ScanResultConsumer>();
    },
    configureBus: (context, cfg) =>
    {
        // Configure topic names for message types
        cfg.Message<ScanResultEvent>(m => m.SetEntityName(TopicNames.ScanResult));
        cfg.Message<TransferApplicationSubmittedEvent>(m => m.SetEntityName(TopicNames.TransferApplicationSubmitted));

        cfg.UseJsonSerializer();
    },
    configureAzureServiceBus: (context, cfg) =>
    {
        cfg.UseJsonSerializer();
        // Azure Service Bus specific configuration
        cfg.SubscriptionEndpoint<ScanResultEvent>($"extweb-{configuration["ApplicationName"]}", e =>
        {
            e.UseMessageRetry(r =>
            {
                // For MessageNotForThisInstanceException (instance filtering in Local env)
                // Retry immediately and frequently so other consumers pick it up fast
                r.Handle<MessageNotForThisInstanceException>();
                r.Immediate(10); // Try 10 times (supports up to 10 concurrent local developers)

                // For all OTHER exceptions (real errors)
                // Retry with delay for transient issues
                r.Ignore<MessageNotForThisInstanceException>(); // Don't apply interval retry to this
                r.Interval(3, TimeSpan.FromSeconds(5)); // 3 retries, 5 seconds apart for real errors
            });

            e.ConfigureConsumeTopology = false;
            e.ConfigureConsumer<ScanResultConsumer>(context);
        });
    });

// Add global exception handler to log crashes before app dies
AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
{
    var exception = args.ExceptionObject as Exception;
    var loggerFactory = builder.Services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("UnhandledException");
    logger.LogCritical(exception, 
        "UNHANDLED EXCEPTION - App is crashing! IsTerminating: {IsTerminating}, Exception Type: {ExceptionType}, Memory: {MemoryMB} MB",
        args.IsTerminating, 
        exception?.GetType().FullName ?? "Unknown",
        GC.GetTotalMemory(false) / 1024 / 1024);
};
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error/ServerError");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
else
{
    // In development, still show custom error page but with more details in logs
    app.UseExceptionHandler("/Error/ServerError");
}

app.UseHttpsRedirection();
app.UseResponseCompression();

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
app.UseCookiePolicy();

app.UseSession();
app.UseHostTemplateResolution();

app.UseStatusCodePages(ctx =>
{
    var c = ctx.HttpContext.Response.StatusCode;
    if (c == 401) ctx.HttpContext.Response.Redirect("/Error/Forbidden");
    else if (c == 403) ctx.HttpContext.Response.Redirect("/Error/Forbidden");
    else if (c == 404) ctx.HttpContext.Response.Redirect("/Error/NotFound");
    else if (c == 500) ctx.HttpContext.Response.Redirect("/Error/ServerError");
    else if (c >= 500 && c < 600) ctx.HttpContext.Response.Redirect("/Error/ServerError"); // All 5xx errors
    return Task.CompletedTask;
});

app.UseAuthentication();
app.UseTokenManagementMiddleware();
app.UseActivityBasedTokenRefresh(); // Session management: idle timeout 30min, absolute timeout 8hr, token refresh at 30min remaining
app.UsePermissionsCache();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

// Redirect root to Dashboard
app.MapGet("/", context =>
{
    context.Response.Redirect("/applications/dashboard");
    return Task.CompletedTask;
});

app.UseGovUkFrontend();

// TokenManagementMiddleware now handles all logout logic internally
// No additional token expiry handlers needed

await app.RunAsync();


[ExcludeFromCodeCoverage]
public static partial class Program { }