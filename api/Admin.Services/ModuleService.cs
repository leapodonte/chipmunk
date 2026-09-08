using Admin.Models;
using Admin.Services.DTO;
using Newtonsoft.Json.Linq;

namespace Admin.Services
{
    /// <summary>
    /// 模块服务类，提供模块相关的操作和功能
    /// </summary>
    public class ModuleService
    {
        private IFreeSql _fsql;

        public ModuleService(IFreeSql fsql)
        {
            _fsql = fsql;
        }

        public async Task<List<Module>> ListAsync(CancellationToken cancellationToken)
        {
            return await _fsql.Select<Module>().OrderBy(a=>a.order).ToTreeListAsync(cancellationToken);
        }

        /// <summary>
        /// 递归枚举模块树，并对每个模块执行指定的操作
        /// </summary>
        /// <param name="tree"></param>
        /// <param name="action"></param>
        /// <param name="cancellationToken"></param>
        public static void EnumTreeModule(List<Module> tree,Action<Module,CancellationToken> action, CancellationToken cancellationToken)
        {
            foreach(var mod in tree)
            {
                action(mod, cancellationToken);
                if(mod.children!=null)
                {
                    EnumTreeModule(mod.children, action, cancellationToken);
                }
            }
        }

        /// <summary>
        /// 更新模块信息
        /// </summary>
        /// <param name="modules"></param>
        public void Update(params Module[] modules)
        {
            _fsql.Transaction(() => {

                foreach (var mod in modules)
                {
                    _fsql.Update<Module>().SetSource(mod).ExecuteAffrows();
                }

            });

        }

        /// <summary>
        /// 获取指定账户的模块数据传输对象（DTO）列表
        /// </summary>
        /// <param name="accId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<List<DtoModule>> GetDtoModulesAsync(Guid accId, CancellationToken cancellationToken)
        {
            var modules = new Dictionary<string, DtoModule>();


            var accMods = await _fsql.Select<AccountModule>()
                    .Where(a => a.account_id == accId && a.module.visible == true)
                    .OrderBy(a => a.module.order)
                    .ToListAsync(cancellationToken);
            foreach (var accMod in accMods)
            {
                var mod = modules.GetValueOrDefault(accMod.module_id);
                if (mod == null)
                {
                    mod = new DtoModule(accMod.module);
                    modules.Add(accMod.module_id, mod);
                }
                mod.MergeActions(accMod.actions);
            }

            return MakeTreeNode(modules, null); ;
        }

        /// <summary>
        /// 获取指定账户的组合模块数据传输对象（DTO）列表，包括账户模块和角色模块
        /// </summary>
        /// <param name="accId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<List<DtoModule>> GetCombinedDtoModulesAsync(Guid accId, CancellationToken cancellationToken)
        {
            var modules = new Dictionary<string, DtoModule>();


            var accMods = await _fsql.Select<AccountModule>()
                    .Where(a => a.account_id == accId && a.module.visible == true)
                    .OrderBy(a => a.module.order)
                    .ToListAsync(cancellationToken);
            foreach (var accMod in accMods)
            {
                var mod = modules.GetValueOrDefault(accMod.module_id);
                if (mod == null)
                {
                    mod = new DtoModule(accMod.module);
                    modules.Add(accMod.module_id, mod);
                }
                mod.MergeActions(accMod.actions);
            }

            var roleMods = await _fsql.Select<RoleModule, AccountRole, Admin.Models.Module, Admin.Models.Role>()
                .LeftJoin((a, b, c, d) => a.role_id == b.role_id)
                .LeftJoin((a, b, c, d) => a.role_id == d.id)
                .RightJoin((a, b, c, d) => a.module_id == c.id)
                .Where((a, b, c, d) => b.account_id == accId && c.visible == true && d.visible == true)
                .ToListAsync(a => new { a.t1, a.t3 }, cancellationToken);

            foreach (var t in roleMods)
            {
                var roleMod = t.t1;
                roleMod.module = t.t3;
                var mod = modules.GetValueOrDefault(roleMod.module_id);
                if (mod == null)
                {
                    mod = new DtoModule(roleMod.module);
                    modules.Add(roleMod.module_id, mod);
                }
                mod.MergeActions(roleMod.actions);
            }


            return MakeTreeNode(modules, null); ;
        }

        /// <summary>
        /// 递归构建模块树节点
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
        /// 设置账户的模块权限，包括添加、更新和删除操作
        /// </summary>
        /// <param name="request"></param>
        public void SetAccountModules(SetAccountModulesRequest request)
        {
            _fsql.Transaction(() => {

                var dels = new List<AccountModule>();
                var adds = new List<AccountModule>();
                var upds = new List<AccountModule>();
                var olds = _fsql.Select<AccountModule>().Where(a => a.account_id == request.acc_id).ToList();

                foreach (var old in olds)
                {
                    var item = request.items.FirstOrDefault(a => a.mod_id == old.module_id);
                    if (item != null)
                    {
                        old.actions = JArray.FromObject(item.actions);
                        upds.Add(old);
                    }
                    else
                    {
                        dels.Add(old);
                    }
                }
                foreach (var item in request.items)
                {
                    if (!olds.Any(a => a.module_id == item.mod_id))
                    {
                        adds.Add(new AccountModule()
                        {
                            id = Guid.NewGuid(),
                            account_id = request.acc_id,
                            actions = JArray.FromObject(item.actions),
                            module_id = item.mod_id,
                        });
                    }
                }
                _fsql.Delete<AccountModule>(dels).ExecuteAffrows();
                _fsql.Update<AccountModule>().SetSource(upds).ExecuteAffrows();
                _fsql.Insert<AccountModule>(adds).ExecuteAffrows();

            });
            

        }
    }
}
