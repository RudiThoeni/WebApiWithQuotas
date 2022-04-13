﻿using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;

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

            //var key = GenerateClientKey(context);
            var (rlConfig, key) = GenerateClientKeyExtended(context, settings.RateLimitConfig);
            if (rlConfig is not null)
            {
                var clientStatistics = await GetClientStatisticsByKey(key);
                
                await context.AddRateLimitHeaders(rlConfig.MaxRequests, clientStatistics == null ? 0 : clientStatistics.NumberOfRequestsCompletedSuccessfully, rlConfig.TimeWindow);

                if (clientStatistics != null)
                    clientStatistics.LastSuccessfulResponseTimeList.RemoveAllExpiredResponseDateTimes(TimeSpan.FromSeconds(rlConfig.TimeWindow), clientStat.LastSuccessfulResponseTime);


                if (clientStatistics != null && DateTime.UtcNow < clientStatistics.LastSuccessfulResponseTime.AddSeconds(rlConfig.TimeWindow) && clientStatistics.NumberOfRequestsCompletedSuccessfully == rlConfig.MaxRequests)
                {
                    //done by WriteasJson
                    //context.Response.Headers.Add("Content-Type", "application/json");                  
                    context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;

                    await context.Response.WriteAsJsonAsync(new QuotaExceededMessage { Message = "quota exceeded", Policy = rlConfig.Type, RetryAfter = rlConfig.TimeWindow, RequestsDone = clientStatistics.NumberOfRequestsCompletedSuccessfully });


                    return;
                }

                await UpdateClientStatisticsStorage(key, rlConfig.MaxRequests, TimeSpan.FromSeconds(rlConfig.TimeWindow));
            }

            await _next(context);
        }

        private static string GenerateClientKey(HttpContext context) => $"{context.Request.Path}_{context.Connection.RemoteIpAddress}";

        private static (RateLimitConfig? rlConfig, string key) GenerateClientKeyExtended(HttpContext context, List<RateLimitConfig> rlsettings)
        {
            RateLimitConfig? ratelimitconfig = default;
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

            var bearertoken = "";
            var loggeduser = "";
            //Check Referer
            if (context.Request.Headers.ContainsKey("Authorization"))
                bearertoken = context.Request.Headers["Authorization"].ToString();

            if(!String.IsNullOrEmpty(bearertoken) && bearertoken.StartsWith("Bearer"))
            {
                var handler = new JwtSecurityTokenHandler();
                var token = bearertoken.Replace("Bearer", "").Trim();

                var jwttoken = handler.ReadJwtToken(token);

                if(jwttoken != null)
                {
                    // Gets name from claims. Generally it's an email address.
                    var usernameClaim = jwttoken.Claims
                        .Where(x => x.Type == ClaimTypes.Name || x.Type == "name")
                        .FirstOrDefault();

                    if(usernameClaim != null)
                        loggeduser = usernameClaim.Value;
                }
            }

            //Check Loggeduser
            //TODO

            //TODO Check if User has Referer, isLogged isAnonymous

            //Case 1 Anonymous, Go to IP Restriction (Maybe on Path?)
            if (String.IsNullOrEmpty(referer) && String.IsNullOrEmpty(loggeduser))
            {
                ratelimitcachekey = $"{context.Request.Path}_{context.Connection.RemoteIpAddress}";
                ratelimitconfig = rlsettings.FirstOrDefault(x => x.Type == "Anonymous");
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
            //No rate limit
            else
            {
                return (null, "");
            }

            return (ratelimitconfig, ratelimitcachekey);
        }

        private async Task<ClientStatistics?> GetClientStatisticsByKey(string key) => await _cache.GetCacheValueAsync<ClientStatistics>(key);

        private async Task UpdateClientStatisticsStorage(string key, int maxRequests, TimeSpan timeWindow)
        {
            var clientStat = await _cache.GetCacheValueAsync<ClientStatistics>(key);

            if (clientStat != null)
            {
                clientStat.LastSuccessfulResponseTimeList.Add(DateTime.UtcNow);
                clientStat.LastSuccessfulResponseTime = DateTime.UtcNow;

                clientStat.LastSuccessfulResponseTimeList.RemoveAllExpiredResponseDateTimes(timeWindow, clientStat.LastSuccessfulResponseTime);

                //if (clientStat.NumberOfRequestsCompletedSuccessfully == maxRequests)
                //    clientStat.NumberOfRequestsCompletedSuccessfully = 1;

                //else
                //    clientStat.NumberOfRequestsCompletedSuccessfully++;

                clientStat.NumberOfRequestsCompletedSuccessfully = clientStat.LastSuccessfulResponseTimeList.Count;

                

                await _cache.SetCacheValueAsync<ClientStatistics>(key, timeWindow, clientStat);
            }
            else
            {
                var clientStatistics = new ClientStatistics
                {
                    LastSuccessfulResponseTime = DateTime.UtcNow,
                    NumberOfRequestsCompletedSuccessfully = 1,
                    LastSuccessfulResponseTimeList = new List<DateTime>() { DateTime.UtcNow }
                };

                await _cache.SetCacheValueAsync(key, timeWindow, clientStatistics);
            }

        }        

        public static MemoryStream GenerateStreamFromString(string value)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(value ?? ""));
        }
    }

    public class ClientStatistics
    {      
        public ClientStatistics()
        {
            LastSuccessfulResponseTimeList = new List<DateTime>();
        }

        public DateTime LastSuccessfulResponseTime { get; set; }
        public int NumberOfRequestsCompletedSuccessfully { get; set; }

        public List<DateTime> LastSuccessfulResponseTimeList { get; set; }
    }

    public class QuotaExceededMessage
    {
        public string? Message { get; set; }
        public string? Hint { 
            get {
                return "https://github.com/noi-techpark/odh-docs/wiki/Api-Quota";
                    } }
        public string? Policy { get; set; }

        public int RetryAfter { get; set; }

        public int RequestsDone { get; set; }
    }

    public static class DateTimeListExtensions
    {
        public static void RemoveAllExpiredResponseDateTimes(this List<DateTime> list, TimeSpan timeWindow, DateTime lastsuccessfulresponsetime)
        {
            //Remove all no more valid Requests                      
            list = list.Where(x => x >= lastsuccessfulresponsetime).ToList();
        }
    }
}
