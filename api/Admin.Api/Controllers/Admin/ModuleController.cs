using Admin.Api.Filters;
using Admin.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Admin.Services;
using Admin.Models;
using Admin.Services.DTO;

namespace Admin.Api.Controllers.Admin
{
    /// <summary>
    /// 后台模块管理
    /// </summary>
    [ApiController, AdminResponse, AdminCheck]
    [Route("admin/[controller]")]
    public class ModuleController : Controller
    {
        private ModuleService moduleService;
        public ModuleController(ModuleService moduleService)
        {
            this.moduleService = moduleService;
        }

        /// <summary>
        /// 获取模块列表
        /// </summary>
        [HttpPost("list")]
        public async Task<Response<List<Module>>> ListAsync(CancellationToken cancellationToken)
        {
            var mods=await moduleService.ListAsync(cancellationToken);
            ModuleService.EnumTreeModule(mods, (mod,cts) => { 
                if(mod.children?.Count==0)
                {
                    mod.children = null;
                }
            }, cancellationToken);
            return Response<List<Module>>.Create(mods);
        }

        /// <summary>
        /// 更新模块
        /// </summary>
        [HttpPost("update")]
        public async Task<Response<object>> UpdateAsync()
        {
            var req=await this.GetRequestAsync<ModuleUpdateRequest>();
            moduleService.Update(req.modules.ToArray());
            return Response<object>.Create(0);
        }

        /// <summary>
        /// 获取帐号模块列表
        /// </summary>
        [HttpPost("account-modules")]
        public async Task<Response<AccountModulesResponse>> AccountModulesAsync(CancellationToken cancellationToken)
        {
            var req = await this.GetRequestAsync<AccountModulesRequest>();
            var items = await moduleService.GetDtoModulesAsync(req.acc_id, cancellationToken);
            return Response<AccountModulesResponse>.Create(new AccountModulesResponse() { modules = items });
        }

        /// <summary>
        /// 获取帐号模块列表（包含父模块）
        /// </summary>
        [HttpPost("combined-modules")]
        public async Task<Response<CombinedModulesResponse>> CombinedModulesAsync(CancellationToken cancellationToken)
        {
            var req = await this.GetRequestAsync<CombinedModulesRequest>();
            var items = await moduleService.GetCombinedDtoModulesAsync(req.acc_id, cancellationToken);
            return Response<CombinedModulesResponse>.Create(new CombinedModulesResponse() { modules = items });
        }

        /// <summary>
        /// 设置帐号模块权限
        /// </summary>
        [HttpPost("set-account-modules")]
        public async Task<Response<object>> SetAccountModulesAsync()
        {
            var req = await this.GetRequestAsync<SetAccountModulesRequest>();
            moduleService.SetAccountModules(req);
            return Response<object>.Create(0);
        }
    }
}
