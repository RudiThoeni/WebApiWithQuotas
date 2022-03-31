# WebApiWithQuotas

## Test Project with Simple Quota Implementation for .Net Core Web Api

- Customizable Response Message
- Rate Limit settings can be applied by config file
- Returns Http Statuscode 429 To many Requests if Quota is exceeded
- Adding Headers (X-Rate-Limit-Limit, X-Rate-Limit-Remaining, X-Rate-Limit-Reset) on each Response

### Different Types of Quotas currently implemented

1. Anonymous User  
IP Restriction, could also applied on path and ip as cachekey
  
2. Known Referer  
Restriction applied to Referer as cachekey
  
3. Known User  
Bearer token is read, Restriction applied to username claim as cachekey
