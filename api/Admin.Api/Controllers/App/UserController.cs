using Admin.Api.Filters;
using Admin.Api.Services;
using Admin.Services;
using Admin.Services.DTO;
using Microsoft.AspNetCore.Mvc;

namespace Admin.Api.Controllers.App
{
    /// <summary>
    /// 前端用户管理
    /// </summary>
    [ApiController, AdminResponse, AppCheck]
    [Route("app/[controller]")]
    public class UserController : ControllerBase
    {
        private UserService _user;
        private AppSessionService _sess;
        public UserController(UserService user, AppSessionService sess)
        {
            _user = user;
            _sess = sess;
        }

        /// <summary>
        /// 微信小程序登录
        /// </summary>
        [HttpPost("wx-mp-login")]
        public async Task<Response<WxLoginResponse>> WxMpLoginAsync(CancellationToken cancellationToken)
        {
            var request=await this.GetRequestAsync<WxLoginRequest>();
            var sess = await _user.WxMpLoginAsync(request.code, cancellationToken);
            _sess.SetSession(sess);

            return Response<WxLoginResponse>.Create(new WxLoginResponse()
            {
                session_id = sess.id
            });
        }

    }
}
