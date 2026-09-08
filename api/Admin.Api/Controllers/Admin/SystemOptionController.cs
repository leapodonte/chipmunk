using Admin.Api.Filters;
using Admin.Services;
using Admin.Services.DTO;
using Microsoft.AspNetCore.Mvc;

namespace Admin.Api.Controllers.Admin
{
    /// <summary>
    /// 后台系统配置管理
    /// </summary>
    [ApiController, AdminResponse, AdminCheck]
    [Route("admin/system-option")]
    public class SystemOptionController : Controller
    {
        private readonly SystemOptionService _sysOptService;

        public SystemOptionController(SystemOptionService sysOptService)
        {
            _sysOptService = sysOptService;
        }

        /// <summary>
        /// 获取系统配置列表
        /// </summary>
        [HttpPost("list")]
        public async Task<Response<ListSystemOptionResponse>> ListAsync(CancellationToken cancellationToken)
        {
            var request = await this.GetRequestAsync<ListSystemOptionRequest>();
            var response=await _sysOptService.ListAsync(request, cancellationToken);

            return Response<ListSystemOptionResponse>.Create(response);
        }

        /// <summary>
        /// 添加系统配置
        /// </summary>
        [HttpPost("add")]
        public async Task<Response<AddSystemOptionResponse>> AddAsync(CancellationToken cancellationToken)
        {
            var request = await this.GetRequestAsync<AddSystemOptionRequest>();

            var response = await _sysOptService.AddAsync(request, cancellationToken);
            return Response<AddSystemOptionResponse>.Create(response);
        }

        /// <summary>
        /// 编辑系统配置
        /// </summary>
        [HttpPost("edit")]
        public async Task<Response<UpdateSystemOptionResponse>> EditAsync(CancellationToken cancellationToken)
        {
            var request = await this.GetRequestAsync<UpdateSystemOptionRequest>();

            var response=await _sysOptService.UpdateAsync(request, cancellationToken);
            return Response<UpdateSystemOptionResponse>.Create(response);
        }

        /// <summary>
        /// 删除系统配置
        /// </summary>
        [HttpPost("delete")]
        public async Task<Response<object>> DeleteAsync(CancellationToken cancellationToken)
        {
            var request = await this.GetRequestAsync<DeleteSystemOptionsRequest>();
            await _sysOptService.RemoveAsync(cancellationToken,request.names);
            return Response<object>.Create(0);
        }
    }
}
