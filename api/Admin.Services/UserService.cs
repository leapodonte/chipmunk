using Admin.Models;
using Admin.Services.DTO;
using Admin.Services.Properties;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Admin.Services
{
    /// <summary>
    /// 用户服务类
    /// </summary>
    public class UserService
    {
        private readonly IFreeSql _fsql;
        private readonly EncryptorService _encryptor;
        private readonly IStringLocalizer<Resources> _localizer;
        private readonly WechatService _wx;

        public UserService(IFreeSql fsql, EncryptorService encryptor, IStringLocalizer<Resources> localizer, WechatService wx)
        {
            _fsql = fsql;
            _encryptor = encryptor;
            _localizer = localizer;
            _wx = wx;
        }

        /// <summary>
        /// 微信小程序登录
        /// </summary>
        /// <param name="code"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<AppSession> WxMpLoginAsync(string code, CancellationToken cancellationToken)
        {
            var wxsess=await _wx.Jscode2SessionAsync(code,cancellationToken);

            var user=await _fsql.Select<User>().Where(a => a.openid == wxsess.openid).FirstAsync();
            if(user==null)
            {
                user = new User();
                user.id=Guid.NewGuid();
                user.openid = wxsess.openid;
                user.unionid = wxsess.unionid;
                user.state = UserState.Active;
                user.type = UserType.WechatMP;
                user.created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                user.updated_at = user.created_at;
                await _fsql.Insert<User>(user).ExecuteAffrowsAsync(cancellationToken);
            }
            else
            {
                user.updated_at= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await _fsql.Update<User>()
                    .Set(a=>a.updated_at, user.updated_at)
                    .ExecuteAffrowsAsync(cancellationToken);
            }

            return new AppSession()
            {
                id=Guid.NewGuid().ToString("N"),
                user=user,
            };
        }
    }
}
