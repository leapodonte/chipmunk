using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Admin.Models;
using Admin.Utils;

namespace Admin.Services
{
    /// <summary>
    /// 系统服务类
    /// </summary>
    public class SystemService
    {
        private IFreeSql _fsql;
        private EncryptorService _encryptor;

        public SystemService(IFreeSql fsql, EncryptorService encryptor)
        {
            _fsql = fsql;
            _encryptor = encryptor;

        }

        /// <summary>
        /// 初始化系统数据库，表结构和一些默认数据。
        /// </summary>
        /// <returns></returns>
        public async Task InitSystemAsync()
        {
            _fsql.CodeFirst.SyncStructure<Module>();
            _fsql.CodeFirst.SyncStructure<Models.Account>();
            _fsql.CodeFirst.SyncStructure<AccountModule>();
            _fsql.CodeFirst.SyncStructure<Role>();
            _fsql.CodeFirst.SyncStructure<RoleModule>();
            _fsql.CodeFirst.SyncStructure<AccountRole>();
            _fsql.CodeFirst.SyncStructure<SystemOption>();
            //_fsql.CodeFirst.SyncStructure<Corp>();
            //_fsql.CodeFirst.SyncStructure<CorpDepartment>();
            //_fsql.CodeFirst.SyncStructure<CorpAccount>();
            _fsql.CodeFirst.SyncStructure<User>();

            await InitModulesAndRolesAsync();
            await InitAdminAsync();
            await InitSystemOptions();
        }

        private async Task InitModulesAndRolesAsync()
        {
            var modules=new List<Module>(10);
            modules.Add(new Module() { id = "home", name = "首页", order = 0, path = "/welcome"});
            modules.Add(new Module() { id = "permission", name = "权限管理", path = "/permission",order = 1});
                modules.Add(new Module() { id = "module", parent_id = "permission", name = "模块管理", order = 0, path = "/permission/module"});
                modules.Add(new Module() { id = "role", parent_id = "permission", name = "角色管理", order = 1, path = "/permission/role"});
            modules.Add(new Module() { id = "corp", name = "企业管理", order = 2, path = "/corp"});
            modules.Add(new Module() { id = "account", name = "帐号管理", order = 3, path = "/account"});
            modules.Add(new Module() { id = "options", name = "系统配置", order = 100, path = "/options"});

            await _fsql.InsertOrUpdate<Module>()
                .SetSource(modules)
                .ExecuteAffrowsAsync();

            {
                var role = new Role() { id = Guid.Empty, name = "系统管理员", visible = true };
                await _fsql.InsertOrUpdate<Role>()
                    .SetSource(role)
                    .ExecuteAffrowsAsync();

                var roleMods = new List<RoleModule>();
                foreach (var module in modules)
                {
                    var roleMod = new RoleModule()
                    {
                        id = Guid.NewGuid(),
                        role_id = role.id,
                        module_id = module.id,
                    };
                    foreach(string? action in module.actions)
                    {
                        if (action != null)
                        {
                            roleMod.actions.Add(action);
                        }
                    }
                    roleMods.Add(roleMod);
                }
                await _fsql.Delete<RoleModule>().Where(a => a.role_id == role.id).ExecuteAffrowsAsync();
                await _fsql.Insert<RoleModule>(roleMods)
                    .ExecuteAffrowsAsync();
            }
        }

        private async Task InitAdminAsync()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string password = _encryptor.EncodePassword("Admin@12345");
            var admin=new Admin.Models.Account() { id = Guid.Empty, name = "admin", nickname = "超级管理员", password = password, active = true, created_at = now, updated_at = now };
           
            await _fsql.InsertOrUpdate<Models.Account>().SetSource(admin)
                .ExecuteAffrowsAsync();

            await _fsql.Delete<AccountRole>().Where(a => a.account_id == admin.id).ExecuteAffrowsAsync();
            await _fsql.Insert<AccountRole>(new AccountRole() { id = Guid.NewGuid(), account_id = Guid.Empty, role_id = Guid.Empty })
                .ExecuteAffrowsAsync();
        }

        private async Task InitSystemOptions()
        {
            string systemId=Guid.NewGuid().ToString("N");
            var opt=new SystemOption() { name = SystemOption.OPT_SystemID, value = systemId,visible=false };
            opt.signature= _encryptor.SignSystemOption(opt);

            await _fsql.InsertOrUpdate<SystemOption>()
                .IfExistsDoNothing()
                .SetSource(opt)
                .ExecuteAffrowsAsync();
        }
    }

}
