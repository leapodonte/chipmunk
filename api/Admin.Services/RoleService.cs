using Admin.Models;
using Admin.Services.DTO;
using Admin.Services.Properties;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Text;

namespace Admin.Services
{
    /// <summary>
    /// 角色服务类
    /// </summary>
    public class RoleService
    {
        private readonly IStringLocalizer<Resources> _localizer;
        private readonly IFreeSql _fsql;

        public RoleService(IFreeSql fsql, IStringLocalizer<Resources> localizer)
        {
            _fsql = fsql;
            _localizer = localizer;
        }

        /// <summary>
        /// 获取角色列表
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ListRoleResponse> ListAsync(ListRoleRequest request, CancellationToken cancellationToken)
        {
            var q = _fsql.Select<Models.Role>();
            if(!string.IsNullOrWhiteSpace(request.search))
            {
                q=q.Where(a=>a.name.StartsWith(request.search));
            }
            q = q.Count(out var total);
            q = q.Page(request.page, request.page_size);

            return new ListRoleResponse()
            {
                total = (ulong)total,
                items = await q.ToListAsync(cancellationToken),
            };
        }

        /// <summary>
        /// 添加角色
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> AddAsync(AddRoleRequest request, CancellationToken cancellationToken)
        {
            var role = new Models.Role()
            {
                id = Guid.NewGuid(),
                name = request.name,
                visible = request.visible,
            };
            int c = await _fsql.Insert<Models.Role>(role).ExecuteAffrowsAsync(cancellationToken);
            return c > 0;
        }

        /// <summary>
        /// 更新角色
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="CodeException"></exception>
        public async Task<bool> UpdateAsync(UpdateRoleRequest request, CancellationToken cancellationToken)
        {
            if (request.id == Guid.Empty)
            {
                throw new CodeException(400, _localizer["SystemDenied"]);
            }

            var q = _fsql.Update<Models.Role>();
            if(request.name!=null)
            {
                q.Set(a => a.name,request.name);
            }
            if(request.visible!=null)
            {
                q.Set(a => a.visible, request.visible);
            }
            q.Where(a => a.id == request.id);
            int c=await q.ExecuteAffrowsAsync(cancellationToken);
            return c > 0;
        }

        /// <summary>
        /// 获取角色详情
        /// </summary>
        /// <param name="id"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<Models.Role?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            return await _fsql.Select<Models.Role>().Where(a => a.id == id).FirstAsync(cancellationToken);
        }

        /// <summary>
        /// 删除角色
        /// </summary>
        /// <param name="ids"></param>
        public void Delete(params Guid[] ids)
        {
            _fsql.Transaction(() => {
                foreach(var id in ids)
                {
                    if (id == Guid.Empty)
                    {
                        continue;
                    }
                    _fsql.Delete<Models.Role>().Where(a => a.id==id).ExecuteAffrows();
                    _fsql.Delete<Models.RoleModule>().Where(a => a.role_id == id).ExecuteAffrows();
                    _fsql.Delete<Models.AccountRole>().Where(a=>a.role_id==id).ExecuteAffrows();
                }
                
            });
        }

        /// <summary>
        /// 设置角色模块权限
        /// </summary>
        /// <param name="request"></param>
        /// <exception cref="CodeException"></exception>
        public void SetRoleModules(SetRoleModulesRequest request)
        {
            if (request.role_id == Guid.Empty)
            {
                throw new CodeException(400, _localizer["SystemDenied"]);
            }

            _fsql.Transaction(() => {
                var dels = new List<RoleModule>();
                var adds = new List<RoleModule>();
                var upds = new List<RoleModule>();
                var olds = _fsql.Select<RoleModule>().Where(a => a.role_id == request.role_id).ToList();

                foreach(var old in olds)
                {
                    var item = request.items.FirstOrDefault(a => a.mod_id == old.module_id);
                    if (item!=null)
                    {
                        old.actions = JArray.FromObject(item.actions);
                        upds.Add(old);
                    }
                    else
                    {
                        dels.Add(old);
                    }
                }
                foreach(var item in request.items)
                {
                    if(!olds.Any(a=>a.module_id==item.mod_id))
                    {
                        adds.Add(new RoleModule()
                        {
                            id = Guid.NewGuid(),
                            role_id = request.role_id,
                            actions = JArray.FromObject(item.actions),
                            module_id = item.mod_id,
                        });
                    }
                }
                _fsql.Delete<RoleModule>(dels).ExecuteAffrows();
                _fsql.Update<RoleModule>().SetSource(upds).ExecuteAffrows();
                _fsql.Insert<RoleModule>(adds).ExecuteAffrows();
            });
            
        }

        /// <summary>
        /// 获取角色模块权限
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<GetRoleModulesResponse> GetRoleModulesAsync(GetRoleModulesRequest request, CancellationToken cancellationToken)
        {
            var modules = new Dictionary<string, DtoModule>();

            var roleMods = await _fsql.Select<RoleModule, Admin.Models.Module>()
                .RightJoin((a, b) => a.module_id == b.id)
                .Where((a, b) => a.role_id == request.role_id && b.visible == true)
                .ToListAsync(a => new { a.t1, a.t2 },cancellationToken);

            foreach (var t in roleMods)
            {
                var roleMod = t.t1;
                roleMod.module = t.t2;
                var mod = modules.GetValueOrDefault(roleMod.module_id);
                if (mod == null)
                {
                    mod = new DtoModule(roleMod.module);
                    modules.Add(roleMod.module_id, mod);
                }
                mod.MergeActions(roleMod.actions);
            }

            return new GetRoleModulesResponse() { items = MakeTreeNode(modules, null) };
        }

        /// <summary>
        /// 生成模块树节点
        /// </summary>
        /// <param name="mods"></param>
        /// <param name="parentId"></param>
        /// <returns></returns>
        private List<DtoModule>? MakeTreeNode(Dictionary<string, DtoModule> mods, string? parentId)
        {
            List<DtoModule> tree = new List<DtoModule>();
            foreach (var mod in mods.Values)
            {
                if (mod.parent_id == parentId)
                {
                    mod.children = MakeTreeNode(mods, mod.id);
                    tree.Add(mod);
                }

            }

            if (tree.Count == 0)
            {
                return null;
            }
            tree.Sort((a, b) =>
            {
                if (!a.order.HasValue || !b.order.HasValue)
                {
                    return 0;
                }
                return a.order.Value.CompareTo(b.order.Value);
            });
            return tree;
        }

        /// <summary>
        /// 获取角色模块权限（不生成树结构）
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<GetRoleModulesResponse> GetRoleModulesAsync1(GetRoleModulesRequest request, CancellationToken cancellationToken)
        {
            var items = await _fsql.Select<Models.RoleModule>()
                .Where(a => a.role_id == request.role_id && a.module.visible).ToListAsync(cancellationToken);

            var mods = new List<DtoModule>(items.Count);
            foreach (var item in items)
            {
                if (item.module == null)
                    continue;


                var mod = new DtoModule(item.module);
                mod.MergeActions(item.actions);
                mods.Add(mod);
            }

            return new GetRoleModulesResponse() { items = mods };
        }

        /// <summary>
        /// 获取账号角色
        /// </summary>
        /// <param name="accId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<List<Models.Role>> GetAccountRolesAsync(Guid accId, CancellationToken cancellationToken)
        {
            var q = _fsql.Select<AccountRole, Models.Role>()
                .RightJoin((a, b) => a.role_id == b.id)
                .Where((a, b) => a.account_id == accId&&b.visible);
            return await q.ToListAsync(a=>a.t2, cancellationToken);
        }

        /// <summary>
        /// 设置账号角色
        /// </summary>
        /// <param name="accId"></param>
        /// <param name="roleIds"></param>
        public void SetAccountRoles(Guid accId, Guid[] roleIds)
        {
            _fsql.Transaction(() => {
                var dels = new List<AccountRole>();
                var adds = new List<AccountRole>();
                var olds=_fsql.Select<AccountRole>().Where(a => a.account_id == accId).ToList();
                
                foreach (var old in olds)
                {
                    if(!roleIds.Contains(old.role_id))
                    {
                        dels.Add(old);
                    }
                }
                foreach (var roleId in roleIds)
                {
                    if(!olds.Any(a=>a.role_id==roleId))
                    {
                        adds.Add(new AccountRole()
                        {
                            id = Guid.NewGuid(),
                            account_id = accId,
                            role_id = roleId,
                        });
                    }
                }

                _fsql.Delete<AccountRole>(dels).ExecuteAffrows();
                _fsql.Insert<AccountRole>(adds).ExecuteAffrows();
            });
        }
    }
}
