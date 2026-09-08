using Admin.Services;
using Admin.Services.DTO;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace Admin.Api
{
    public static class ControllerBaseExtensions
    {
        public static AdminSession? GetSession(this ControllerBase controller)
        {
            return controller.HttpContext.Items["session"] as AdminSession;
        }

        public static async Task<TBody> GetRequestAsync<TBody>(this ControllerBase controller,bool body=true)
            where TBody : Request
        {
            string bodyStr = await controller.Request.ReadBodyAsync();
            TBody? request = JsonSerializer.Deserialize<TBody>(bodyStr);
            if (request == null)
            {
                throw new CodeException(400, "参数不能为空");
            }
            request.Validate();
            return request;
        }

        public static async Task<string> ReadBodyAsync(this HttpRequest request)
        {
            request.EnableBuffering();
            using (MemoryStream ms = new MemoryStream((int)request.Body.Length))
            {
                request.Body.Position = 0;
                await request.Body.CopyToAsync(ms);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }
}
