using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using DfE.ExternalApplications.Web.Services;

namespace DfE.ExternalApplications.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AutocompleteController : ControllerBase
    {
        private readonly IAutocompleteService _autocompleteService;
        private readonly ILogger<AutocompleteController> _logger;

        public AutocompleteController(IAutocompleteService autocompleteService, ILogger<AutocompleteController> logger)
        {
            _autocompleteService = autocompleteService;
            _logger = logger;
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string endpoint, [FromQuery] string query)
        {
            _logger.LogInformation("Autocomplete search called with endpoint: {Endpoint}, query: {Query}", endpoint, query);
            
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                _logger.LogWarning("Autocomplete search called without endpoint");
                return BadRequest("Endpoint parameter is required");
            }

            try
            {
                var results = await _autocompleteService.SearchAsync(endpoint, query);
                _logger.LogInformation("Autocomplete search returned {Count} results", results.Count);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in autocomplete search endpoint: {Endpoint}, query: {Query}", endpoint, query);
                return Ok(new List<string>());
            }
        }
    }
} 