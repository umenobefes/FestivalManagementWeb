using System;
using System.Threading.Tasks;
using FestivalManagementWeb.Services;
using Microsoft.Extensions.Options;
using FestivalManagementWeb.Models;
using Microsoft.AspNetCore.Http;

namespace FestivalManagementWeb.Middleware
{
    public class RequestQuotaMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IFreeTierService _freeTier;
        private readonly IRequestQuotaService _quota;
        private readonly IOptionsMonitor<FreeTierSettings> _options;

        public RequestQuotaMiddleware(RequestDelegate next, IFreeTierService freeTier, IRequestQuotaService quota, IOptionsMonitor<FreeTierSettings> options)
        {
            _next = next;
            _freeTier = freeTier;
            _quota = quota;
            _options = options;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Exclude health endpoints if any
            var path = context.Request.Path.Value ?? string.Empty;
            if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // Compute today's cap from monthly remaining per-day
            var info = _freeTier.GetInfo();
            var dailyCap = (int)Math.Max(0, Math.Floor(info.RequestsPerDayRemaining));
            var enforce = _options.CurrentValue?.EnforceRequestDailyCap ?? false;
            var capForCount = enforce ? dailyCap : int.MaxValue;

            if (!_quota.TryConsume(1, capForCount, out var usedAfter, out var remaining))
            {
                var nowUtc = DateTime.UtcNow;
                var midnightUtc = nowUtc.Date.AddDays(1);
                var retryAfter = (int)Math.Max(1, (midnightUtc - nowUtc).TotalSeconds);

                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers["Retry-After"] = retryAfter.ToString();
                await context.Response.WriteAsync($"Daily request limit reached. Try again after {midnightUtc:O} UTC.");
                return;
            }

            await _next(context);
        }
    }
}
