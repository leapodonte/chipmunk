using Admin.Api.Services;
using Admin.Services;
using Admin.Services.DTO;
using Admin.Services.Properties;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using System.Text;
using System.Text.Json;

namespace Admin.Api.Filters
{
    public class AdminResponseAttribute: ActionFilterAttribute
    {
        private bool IsPrint(string url)
        {
            string[] wl = {
            };

            return !wl.Any(e => url.EndsWith(e));
        }

        public override void OnActionExecuted(ActionExecutedContext context)
        {
            var log = context.HttpContext.RequestServices.GetRequiredService<AdminLogService>();
            var _localizer = context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<Resources>>();
            StringBuilder sb = PrintRequest(null, context);


            var controller = context.Controller as ControllerBase;
            if (context.Exception is AggregateException)
            {
                var ex = context.Exception as AggregateException;
                context.Exception = ex.InnerException;
            }

            if (context.Exception != null)
            {
                if (sb != null)
                {
                    ///报错时写入请求日志
                    log.Default.Debug(sb.ToString());

                }

                Response<object> result;

                //判断错误类型进行处理
                if (context.Exception is CodeException ex)
                {
                    result = Response<object>.Create(ex.Code, ex.Message);
                }
                else
                {
                    result = Response<object>.Create(500, _localizer["InternalServerError"]);
                }
                log.Default.Error("", context.Exception);
                context.Result = new JsonResult(result);
                context.ExceptionHandled = true;

            }
            else
            {
                sb = PrintResponse(sb, context);
                if (sb != null)
                {
                    log.Default.Debug(sb.ToString());
                }
            }
            base.OnActionExecuted(context);
        }


        private StringBuilder PrintRequest(StringBuilder sb, ActionExecutedContext context)
        {
            if (!IsPrint(context.HttpContext.Request.Path.ToString()))
                return null;

            if (sb == null)
                sb = new StringBuilder();
            sb.AppendLine("================================");
            sb.AppendLine("请求:");
            sb.Append(context.HttpContext.Request.Method).Append(" ");
            sb.AppendLine(context.HttpContext.Request.Path.ToString() + context.HttpContext.Request.QueryString);
            sb.AppendLine();
            sb.AppendLine("Headers:");
            foreach (var header in context.HttpContext.Request.Headers)
            {
                sb.AppendLine($"{header.Key}: {header.Value}");
            }
            sb.AppendLine();
            sb.AppendLine("Body:");
            if (context.HttpContext.Request.HasFormContentType)
            {
                foreach (var kv in context.HttpContext.Request.Form)
                {
                    sb.AppendLine($"{kv.Key}: {kv.Value}");
                }
            }
            else
            {
                string s = context.HttpContext.Request.ReadBodyAsync().Result;
                sb.AppendLine(s);
            }
            sb.AppendLine();


            return sb;
        }

        private StringBuilder PrintResponse(StringBuilder sb, ActionExecutedContext context)
        {
            if (!IsPrint(context.HttpContext.Request.Path.ToString()))
                return null;

            //if (Log.Default.IsDebugEnabled)
            //{
            if (sb == null)
                sb = new StringBuilder();

            sb.AppendLine("响应:");

            var r = context.Result as ObjectResult;

            if (r != null && r.Value != null)
            {
                sb.AppendLine(JsonSerializer.Serialize(r.Value));
            }
            //}
            return sb;
        }
    }
}
