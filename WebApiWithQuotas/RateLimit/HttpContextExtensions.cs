﻿namespace WebApiWithQuotas.RateLimit
{
    public static class HttpContextExtensions
    {
        public async static Task AddRateLimitHeaders(this HttpContext context, int maxrequests, int requestsdone, int timewindow, CancellationToken token = default)
        {
            var remainingrequests = maxrequests - requestsdone;

            context.Response.Headers.Add("Content-Type", "application/json");
            context.Response.Headers.Add("X-Rate-Limit-Limit", maxrequests.ToString());
            context.Response.Headers.Add("X-Rate-Limit-Remaining", remainingrequests.ToString());
            context.Response.Headers.Add("X-Rate-Limit-Reset", timewindow.ToString());


        }
    }
}
