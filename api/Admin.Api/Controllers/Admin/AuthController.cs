using Admin.Api.Filters;
using Admin.Api.Services;
using Admin.Services;
using Admin.Services.DTO;
using Admin.Services.Properties;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;

namespace Admin.Api.Controllers.Admin
{
    /// <summary>
    /// 后台管理员帐号登录授权
    /// </summary>
    [ApiController, AdminResponse, AdminCheck]
    [Route("admin/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IStringLocalizer<Resources> _localizer;
        private readonly AdminSessionService _sessionService;

        public AuthController(AdminSessionService sessionService, IStringLocalizer<Resources> localizer)
        {
            _sessionService = sessionService;
            _localizer = localizer;
        }

        /// <summary>
        /// 登录
        /// </summary>
        [HttpPost("login")]
        public async Task<Response<LoginResponse>> LoginAsync(CancellationToken cancellationToken)
        {
            var request = await this.GetRequestAsync<LoginRequest>();
            var response=await _sessionService.LoginAsync(request, cancellationToken);

            return Response<LoginResponse>.Create(response); ;
        }

        /// <summary>
        /// 退出登录
        /// </summary>
        [HttpPost("logout")]
        public Response<object> Logout()
        {
            string? sessionId = this.GetSession()?.id;
            _sessionService.Logout(sessionId);
            return Response<object>.Create(0, null);
        }

        /// <summary>
        /// 获取当前登录用户信息
        /// </summary>
        [HttpPost("current-user")]
        public async Task<Response<object>> CurrentUserAsync()
        {
            AdminSession? session = this.GetSession();
            if (session == null)
            {
                throw new CodeException(401, _localizer["InvalidStream"]);
            }


            return Response<object>.Create(new {account= session.account, fcp=session?.firs_changed_pwd});
        }

        /// <summary>
        /// 获取当前登录用户的模块权限
        /// </summary>
        [HttpPost("current-user-modules")]
        public Response<CurrentUserModulesResponse> CurrentUserModules()
        {
            var session = this.GetSession();
            if (session == null)
            {
                throw new CodeException(401, _localizer["InvalidStream"]);
            }

            return Response<CurrentUserModulesResponse>.Create(new CurrentUserModulesResponse { modules = session.modules });
        }

        /// <summary>
        /// 获取当前登录用户的角色权限
        /// </summary>
        public async Task<Response<CurrentUserRolesResponse>> CurrentUserRolesAsync(CancellationToken cancellationToken)
        {
            var session = this.GetSession();
            if (session == null)
            {
                throw new CodeException(401, _localizer["InvalidStream"]);
            }

            var response =await _sessionService.CurrentUserRolesAsync(session,cancellationToken);

            return Response<CurrentUserRolesResponse>.Create(response);
        }

        /// <summary>
        /// 修改密码
        /// </summary>
        [HttpPost("change-pwd")]
        public async Task<Response<object>> ChangePasswordAsync(CancellationToken cancellationToken)
        {
            var request = await this.GetRequestAsync<ChangePasswordRequest>();
            var session = this.GetSession();
            if (session == null)
            {
                throw new CodeException(401, _localizer["InvalidStream"]);
            }
            await _sessionService.ChangePassword(session, request, cancellationToken);
            return Response<object>.Create(0);
        }
        
    }
}
