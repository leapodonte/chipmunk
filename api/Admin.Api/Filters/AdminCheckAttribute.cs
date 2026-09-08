using Admin.Api.Services;
using Admin.Services;
using Admin.Services.Properties;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;

namespace Admin.Api.Filters
{
    public class AdminCheckAttribute: ActionFilterAttribute
    {

        private readonly string[] noCheckingSessionActions_ = [
            "/admin/auth/login"
            ];

        private bool DontCheckSession(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            foreach (string s in noCheckingSessionActions_)
            {
                if (path.EndsWith(s))
                    return true;
            }

            return false;
        }

        private bool IsPrint(string url)
        {
            string[] wl = {
            };

            return !wl.Any(e => url.EndsWith(e));
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            string? signature = context.HttpContext.Request.Headers["Admin-Signature"];
            string? timestamp = context.HttpContext.Request.Headers["Admin-Timestamp"];
            string? sessionId = context.HttpContext.Request.Headers["Admin-Session"];
            string? requestId= context.HttpContext.Request.Headers["Admin-RequestId"];
            context.HttpContext.Response.Headers["Admin-RequestId"] = requestId;

            var _localizer=context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<Resources>>();
            try
            {
                if (!context.HttpContext.Request.HasFormContentType)
                {
                    if (!long.TryParse(timestamp, out long ts))
                    {
                        throw new CodeException(402, _localizer["SignFailed"]);
                    }
                    //var controller=context.Controller as ControllerBase;

                    if (string.IsNullOrWhiteSpace(signature))
                    {
                        throw new CodeException(402, _localizer["SignFailed"]);
                    }

                    var encService = context.HttpContext.RequestServices.GetRequiredService<EncryptorService>();

                    string body = context.HttpContext.Request.ReadBodyAsync().Result;
                    string signature1 = encService.SignRequestBody(body, ts);
                    if (signature1 != signature)
                    {
                        throw new CodeException(402, _localizer["SignFailed"]);
                    }

                    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    if (Math.Abs(now - ts) >= 15 * 60)
                    {
                        throw new CodeException(402, _localizer["SignFailedByTimestamp"]);
                    }
                }
                string path = context.HttpContext.Request.Path.ToString();
                if (!DontCheckSession(path))
                {
                    if (string.IsNullOrWhiteSpace(sessionId))
                    {
                        throw new CodeException(401, _localizer["InvalidSession"]);
                    }
                    var sessionService = context.HttpContext.RequestServices.GetRequiredService<AdminSessionService>();
                    var session = sessionService.GetSession(sessionId);
                    if (session == null)
                    {
                        throw new CodeException(401, _localizer["InvalidSession"]);
                    }
                    context.HttpContext.Items["session"] = session;
                }

                
            }
            finally
            {
                base.OnActionExecuting(context);
            }
        }
        
    }
}
