using System.Threading.Tasks;
using FestivalManagementWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace FestivalManagementWeb.Filters
{
    public sealed class CosmosCapacityGuardAttribute : TypeFilterAttribute
    {
        public CosmosCapacityGuardAttribute() : base(typeof(CosmosCapacityGuardFilter))
        {
        }
    }

    internal sealed class CosmosCapacityGuardFilter : IAsyncActionFilter
    {
        private readonly IFreeTierService _freeTierService;
        private readonly ILogger<CosmosCapacityGuardFilter> _logger;

        public CosmosCapacityGuardFilter(IFreeTierService freeTierService, ILogger<CosmosCapacityGuardFilter> logger)
        {
            _freeTierService = freeTierService;
            _logger = logger;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var info = _freeTierService.GetInfo();
            var cosmos = info?.Cosmos;

            if (cosmos is { Enabled: true, ShouldStop: true })
            {
                _logger.LogWarning("Blocking write because Cosmos free-tier capacity is exceeded.");
                context.Result = new RedirectToActionResult("CapacityLimit", "FreeTier", null);
                return;
            }

            await next();
        }
    }
}
