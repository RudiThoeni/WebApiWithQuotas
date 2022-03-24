﻿using Microsoft.Extensions.Caching.Distributed;
using System.Net;

namespace WebApiWithQuotas.RateLimit
{
    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IDistributedCache _cache;

        public RateLimitingMiddleware(RequestDelegate next, IDistributedCache cache)
        {
            _next = next;
            _cache = cache;
        }

        public async Task InvokeAsync(HttpContext context, ISettings settings)
        {
            var ratelimitconfig = settings.RateLimitConfig;
            var endpoint = context.GetEndpoint();

   
            //var rateLimitingDecorator = endpoint?.Metadata.GetMetadata<LimitRequests>();

            //If no config present do nothing
            if (ratelimitconfig is null)
            {
                await _next(context);
                return;
            }

            var rlResult = GenerateClientKeyExtended(context, settings.RateLimitConfig);

            //var key = GenerateClientKey(context);
            var key = rlResult.Item2;
            var rlConfig = rlResult.Item1;

            var clientStatistics = await GetClientStatisticsByKey(key);

            if (clientStatistics != null && DateTime.UtcNow < clientStatistics.LastSuccessfulResponseTime.AddSeconds(rlConfig.TimeWindow) && clientStatistics.NumberOfRequestsCompletedSuccessfully == rlConfig.MaxRequests)
            {
                await context.Response.WriteAsJsonAsync(new QuotaExceededMessage { Message = "quota exceeded", RequestorType = rlConfig.Type });
                context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                return;
            }

            await UpdateClientStatisticsStorage(key, rlConfig.MaxRequests);
            await _next(context);
        }

        private static string GenerateClientKey(HttpContext context) => $"{context.Request.Path}_{context.Connection.RemoteIpAddress}";

        private static Tuple<RateLimitConfig, string> GenerateClientKeyExtended(HttpContext context, List<RateLimitConfig> rlsettings)
        {
            RateLimitConfig ratelimitconfig = default(RateLimitConfig);
            string ratelimitcachekey = "";

            var referer = "";

            //Check Referer
            if (context.Request.Headers.ContainsKey("Referer"))
                referer = context.Request.Headers["Referer"].ToString();
            else
            {
                //Search the QS for Referer
                if (context.Request.Query.ContainsKey("Referer"))
                    referer = context.Request.Query["Referer"].ToString();
            }

            var loggeduser = "";

            //Check Loggeduser
            //TODO

            //TODO Check if User has Referer, isLogged isAnonymous

            //Case 1 Anonymous, Go to IP Restriction (Maybe on Path?)
            if(String.IsNullOrEmpty(referer) && String.IsNullOrEmpty(loggeduser))
            {
                ratelimitcachekey = $"{context.Request.Path}_{context.Connection.RemoteIpAddress}";
                ratelimitconfig = rlsettings.Where(x => x.Type == "Anonymous").FirstOrDefault();
            }
            //Case 2 Referer passed generate key with Referer
            else if (!String.IsNullOrEmpty(referer) && String.IsNullOrEmpty(loggeduser))
            {
                ratelimitcachekey = $"{referer}";
                ratelimitconfig = rlsettings.Where(x => x.Type == "Referer").FirstOrDefault();
            }

            //Case 3 Logged user, decode token and use username as key
            else if (!String.IsNullOrEmpty(loggeduser))
            {
                ratelimitcachekey = $"{loggeduser}";
                ratelimitconfig = rlsettings.Where(x => x.Type == "Logged").FirstOrDefault();
            }

            return Tuple.Create(ratelimitconfig, ratelimitcachekey);
        }

        private async Task<ClientStatistics> GetClientStatisticsByKey(string key) => await _cache.GetCacheValueAsync<ClientStatistics>(key);

        private async Task UpdateClientStatisticsStorage(string key, int maxRequests)
        {
            var clientStat = await _cache.GetCacheValueAsync<ClientStatistics>(key);

            if (clientStat != null)
            {
                clientStat.LastSuccessfulResponseTime = DateTime.UtcNow;

                if (clientStat.NumberOfRequestsCompletedSuccessfully == maxRequests)
                    clientStat.NumberOfRequestsCompletedSuccessfully = 1;

                else
                    clientStat.NumberOfRequestsCompletedSuccessfully++;

                await _cache.SetCahceValueAsync<ClientStatistics>(key, clientStat);
            }
            else
            {
                var clientStatistics = new ClientStatistics
                {
                    LastSuccessfulResponseTime = DateTime.UtcNow,
                    NumberOfRequestsCompletedSuccessfully = 1
                };

                await _cache.SetCahceValueAsync<ClientStatistics>(key, clientStatistics);
            }

        }
    }

    public class ClientStatistics
    {
        public DateTime LastSuccessfulResponseTime { get; set; }
        public int NumberOfRequestsCompletedSuccessfully { get; set; }
    }

    public class QuotaExceededMessage
    {
        public string Message { get; set; }
        public string MoreInfos { get; set; }

        public string RequestorType { get; set; }
    }
}
