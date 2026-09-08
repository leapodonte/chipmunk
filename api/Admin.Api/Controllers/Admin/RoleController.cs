using Admin.Api.Filters;
using Admin.Services;
using Admin.Services.DTO;
using Admin.Services.Properties;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Admin.Api.Controllers.Admin
{
    /// <summary>
    /// 后台角色管理
    /// </summary>
    [ApiController, AdminResponse, AdminCheck]
    [Route("admin/[controller]")]
    public class RoleController : Controller
    {
        private readonly IStringLocalizer<Resources> _localizer;
        private readonly RoleService _roleService;
        public RoleController(RoleService roleService, IStringLocalizer<Resources> localizer)
        {
            _roleService = roleService;
            _localizer = localizer;
        }

        /// <summary>
        /// 获取角色列表
        /// </summary>
        [HttpPost("list")]
        public async Task<Response<ListRoleResponse>> ListAsync(CancellationToken cancellationToken)
        {
            var request=await this.GetRequestAsync<ListRoleRequest>();

            var response=await _roleService.ListAsync(request, cancellationToken);
            
            return Response<ListRoleResponse>.Create(response);
        }

        /// <summary>
        /// 添加角色
        /// </summary>
        [HttpPost("add")]
        public async Task<Response<object>> AddAsync(CancellationToken cancellationToken)
        {
            var req = await this.GetRequestAsync<AddRoleRequest>();
            await _roleService.AddAsync(req, cancellationToken);
            return Response<object>.Create(0);
        }

        /// <summary>
        /// 编辑角色
        /// </summary>
        [HttpPost("update")]
        public async Task<Response<object>> UpdateAsync(CancellationToken cancellationToken)
        {
            var req=await this.GetRequestAsync<UpdateRoleRequest>();
            
            await _roleService.UpdateAsync(req, cancellationToken);
            return Response<object>.Create(0);
        }

        /// <summary>
        /// 获取角色信息
        /// </summary>
        [HttpPost("get")]
        public async Task<Response<GetRoleResponse>> GetAsync(CancellationToken cancellationToken)
        {
            var req = await this.GetRequestAsync<GetRoleRequest>();

            var role=await _roleService.GetAsync(req.id, cancellationToken);
            if(role==null)
            {
                return Response<GetRoleResponse>.Create(0, _localizer["NotFound"]);
            }
            return Response<GetRoleResponse>.Create(new GetRoleResponse() { item=role});
        }

        /// <summary>
        /// 删除角色
        /// </summary>
        [HttpPost("delete")]
        public async Task<Response<object>> DeleteAsync()
        {
            var req = await this.GetRequestAsync<DeleteRolesRequest>();
            _roleService.Delete(req.ids!);
            return Response<object>.Create(0);
        }

        /// <summary>
        /// 设置角色模块权限
        /// </summary>
        [HttpPost("set-modules")]
        public async Task<Response<object>> SetModulesAsync()
        {
            var req = await this.GetRequestAsync<SetRoleModulesRequest>();
            
            _roleService.SetRoleModules(req);
            return Response<object>.Create(0);
        }

        /// <summary>
        /// 获取角色模块权限
        /// </summary>
        [HttpPost("get-modules")]
        public async Task<Response<GetRoleModulesResponse>> GetModulesAsync(CancellationToken cancellationToken)
        {
            var req = await this.GetRequestAsync<GetRoleModulesRequest>();

            var rsp=await _roleService.GetRoleModulesAsync(req, cancellationToken);
            return Response<GetRoleModulesResponse>.Create(rsp);
        }

        /// <summary>
        /// 获取帐号角色列表
        /// </summary>
        [HttpPost("account-roles")]
        public async Task<Response<GetAccountRolesResponse>> GetAccountRolesAsync(CancellationToken cancellationToken)
        {
            var req = await this.GetRequestAsync<GetAccountRolesRequest>();
            var items = await _roleService.GetAccountRolesAsync(req.acc_id, cancellationToken);
            return Response<GetAccountRolesResponse>.Create(new GetAccountRolesResponse() { items = items });
        }

        /// <summary>
        /// 设置帐号角色
        /// </summary>
        [HttpPost("set-account-roles")]
        public async Task<Response<object>> SetAccountRolesAsync()
        {
            var req = await this.GetRequestAsync<SetAccountRoleRequest>();
            _roleService.SetAccountRoles(req.acc_id,req.role_ids);
            return Response<object>.Create(0);
        }
    }
}
