using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Infrastructure.Parsers;
using DfE.ExternalApplications.Infrastructure.Providers;
using DfE.ExternalApplications.Infrastructure.Services;
using DfE.ExternalApplications.Infrastructure.Stores;
using DfE.ExternalApplications.Web.Services;
using DfE.ExternalApplications.Web.Interfaces;
using GovUK.Dfe.ExternalApplications.Api.Client;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using GovUK.Dfe.ExternalApplications.Api.Client.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.ExternalApplications.Web.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddExternalApplicationsApiClients(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddExternalApplicationsApiClient<ITokensClient, TokensClient>(configuration);
            services.AddExternalApplicationsApiClient<IUsersClient, UsersClient>(configuration);
            services.AddExternalApplicationsApiClient<IApplicationsClient, ApplicationsClient>(configuration);
            services.AddExternalApplicationsApiClient<ITemplatesClient, TemplatesClient>(configuration);
            services.AddExternalApplicationsApiClient<IHubAuthClient, HubAuthClient>(configuration);
            services.AddExternalApplicationsApiClient<INotificationsClient, NotificationsClient>(configuration);
            return services;
        }

        public static IServiceCollection AddWebLayerServices(this IServiceCollection services)
        {
            // Web layer services
            services.AddScoped<IFieldRendererService, FieldRendererService>();
            services.AddScoped<IFormErrorStore, FormErrorStore>();

            // Infrastructure/application services used by web
            services.AddScoped<IApplicationResponseService, ApplicationResponseService>();
            services.AddScoped<IFieldFormattingService, FieldFormattingService>();
            services.AddScoped<ITemplateManagementService, TemplateManagementService>();
            services.AddScoped<IApplicationStateService, ApplicationStateService>();
            services.AddScoped<IFileUploadService, FileUploadService>();
            services.AddScoped<IAutocompleteService, AutocompleteService>();
            services.AddScoped<IComplexFieldConfigurationService, ComplexFieldConfigurationService>();
            services.AddScoped<IComplexFieldRendererFactory, ComplexFieldRendererFactory>();
            services.AddScoped<IComplexFieldRenderer, AutocompleteComplexFieldRenderer>();
            services.AddScoped<IComplexFieldRenderer, CompositeComplexFieldRenderer>();
            services.AddScoped<IComplexFieldRenderer, UploadComplexFieldRenderer>();
            services.AddSingleton<ITemplateStore, ApiTemplateStore>();
            services.AddSingleton<IFormTemplateParser, JsonFormTemplateParser>();
            services.AddScoped<IFormTemplateProvider, FormTemplateProvider>();
            
            // Form Engine Services
            services.AddScoped<IFormStateManager, FormStateManager>();
            services.AddScoped<IFormNavigationService, FormNavigationService>();
            services.AddScoped<IFormDataManager, FormDataManager>();
            services.AddScoped<IFormValidationOrchestrator, FormValidationOrchestrator>();
            services.AddScoped<IFormConfigurationService, FormConfigurationService>();
            return services;
        }
    }
}


