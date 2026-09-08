using Admin.Models;
using Admin.Services;
using Admin.Services.DTO;
using Admin.Services.Properties;
using FreeRedis;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Text.Json;
using System.Threading;
using static System.Net.WebRequestMethods;

namespace Admin.Api.Services
{
    /// <summary>
    /// 后台管理员会话服务
    /// </summary>
    public class AdminSessionService
    {
        public const string KEY = "admin:sessions";
        public readonly TimeSpan TS = TimeSpan.FromMinutes(60);
        private readonly RedisClient _redis;

        private readonly IFreeSql _fsql;
        private readonly IStringLocalizer<Resources> _localizer;
        private readonly EncryptorService _encryptor;
        private readonly AccountService _accountService;
        private readonly RoleService _roleService;
        private readonly ModuleService _moduleService;
        private readonly SystemOptionService _systemOptionService;

        public AdminSessionService(
            IConfiguration cfg,
            RedisClient redis,
            IFreeSql fsql, 
            IStringLocalizer<Resources> localizer, 
            EncryptorService encryptor, 
            AccountService accountService,
            RoleService roleService,
            ModuleService moduleService,
            SystemOptionService systemOptionService)
        {
            _redis = redis;
            _fsql = fsql;
            _localizer = localizer;
            _encryptor = encryptor;
            _accountService = accountService;
            _systemOptionService = systemOptionService;
            _roleService = roleService;
            _moduleService = moduleService;
        }
        
        public bool SetSession(AdminSession session)
        {
            string key = $"{KEY}:{session.id}";
            string val=JsonSerializer.Serialize(session);
            return _redis.Set(key,val, TS,false,false,false,false)=="OK";
        }

        public bool CheckSession(string id)
        {
            string key = $"{KEY}:{id}";
            
            return _redis.Expire(key, TS);
        }

        public AdminSession? GetSession(string? id)
        {
            if(id == null)
                return null;
            string key = $"{KEY}:{id}";

            if(!_redis.Expire(key, TS))
            {
                return null;
            }

            string? val=_redis.Get(key);
            if(string.IsNullOrWhiteSpace(val))
            {
                return null;
            }
            
            return JsonSerializer.Deserialize<AdminSession>(val);
        }

        public bool RemoveSession(string? id)
        {
            if (id == null)
                return false;

            string key = $"{KEY}:{id}";
            return _redis.Del(key)>0;
        }

        public async Task<LoginResponse> LoginAsync(LoginRequest request,CancellationToken cancellationToken)
        {
            var acc = await _accountService.LoginAsync(request,cancellationToken);

            AdminSession session = new AdminSession()
            {
                id = Guid.NewGuid().ToString("n"),
                account = new DtoAccount(acc),
                modules = await _moduleService.GetCombinedDtoModulesAsync(acc.id,cancellationToken),
            };

            if (acc.id == Guid.Empty)
            {
                session.firs_changed_pwd = await _systemOptionService.IsChangedPasswordAsync(cancellationToken);
            }
            if(!SetSession(session))
            {
                throw new CodeException(500, _localizer["InternalServerError"]);
            }

            

            return new LoginResponse()
            {
                session = session.id,
                fcp= session.firs_changed_pwd,
            };

        }

        public bool Logout(string? sessionId)
        {
            return RemoveSession(sessionId);
        }



        public async Task<CurrentUserRolesResponse> CurrentUserRolesAsync(AdminSession? session, CancellationToken cancellationToken)
        {
            var items = await _roleService.GetAccountRolesAsync(session.account.id.Value, cancellationToken);
            return new CurrentUserRolesResponse { items = items };
        }

        public async Task ChangePassword(AdminSession session,ChangePasswordRequest request, CancellationToken cancellationToken)
        {
            var acc=await _fsql.Select<Account>().Where(a => a.id == session.account.id).FirstAsync();
            if(acc == null)
            {
                throw new CodeException(404, _localizer["NotFound"]);
            }

            string oldPwd=_encryptor.EncodePasswordServer(request.old_pwd);
            if(acc.password!=oldPwd)
            {
                throw new CodeException(404, _localizer["LoginFailed"]);
            }

            UpdateAccountRequest req = new UpdateAccountRequest()
            {
                id = acc.id,
                password = request.new_pwd,
            };
            await _accountService.UpdateAsync(req,cancellationToken);
            if (acc.id == Guid.Empty)
            {
                var opt = new SystemOption()
                { 
                    name = SystemOption.OPT_ChangedPwd,
                    value="1",
                    visible=false,
                };
                await _systemOptionService.SetAsync(opt,cancellationToken);
                session.firs_changed_pwd = true;
                if(!SetSession(session))
                {
                    throw new CodeException(500, _localizer["InternalServerError"]);
                }
            }
        }




    }
}
