﻿using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Web.Pages
{
    [ExcludeFromCodeCoverage]
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        public string? Email { get; private set; }
        public string? FirstName { get; private set; }
        public string? LastName { get; private set; }
        public string? OrganisationName { get; private set; }
        private readonly IApplicationsClient _applicationsClient;


        public IndexModel(ILogger<IndexModel> logger, IApplicationsClient usersClient)
        {
            _logger = logger;
            _applicationsClient = usersClient;
        }

        public async Task OnGet()
        {

            _applicationsClient.GetMyApplicationsAsync();

            Email = User.FindFirst(ClaimTypes.Email)?.Value
                    ?? User.FindFirst("email")?.Value;

            FirstName = User.FindFirst(ClaimTypes.GivenName)?.Value;
            LastName = User.FindFirst(ClaimTypes.Surname)?.Value;

            var orgJson = User.FindFirst("organisation")?.Value;
            if (!string.IsNullOrEmpty(orgJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(orgJson);
                    OrganisationName = doc.RootElement
                        .GetProperty("name")
                        .GetString();
                }
                catch
                {
                    OrganisationName = null;
                }
            }

        }
    }
    }