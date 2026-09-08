using Admin.Api.Filters;
using Admin.Models;
using Admin.Services;
using Admin.Services.DTO;
using Admin.Services.Properties;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Admin.Api.Controllers.Admin
{
    /// <summary>
    /// 后台企业管理
    /// </summary>
    [ApiController, AdminResponse, AdminCheck]
    [Route("admin/[controller]")]
    public class CorpController : Controller
    {
        private readonly IStringLocalizer<Resources> _localizer;
        private readonly CorpService _corpService;

        public CorpController(CorpService corpService, IStringLocalizer<Resources> localizer)
        {
            _localizer = localizer;
            _corpService = corpService;
        }

        /// <summary>
        /// 获取企业列表
        /// </summary>
        [HttpPost("list")]
        public async Task<Response<ListCorpResponse>> ListAsync(CancellationToken cancellationToken)
        {
            var request = await this.GetRequestAsync<ListCorpRequest>();

            var response=await _corpService.ListAsync(request, cancellationToken);
            return Response<ListCorpResponse>.Create(response);
        }

        /// <summary>
        /// 添加企业
        /// </summary>
        [HttpPost("add")]
        public async Task<Response<AddCorpResponse>> AddAsync(CancellationToken cancellationToken)
        {
            var request = await this.GetRequestAsync<AddCorpRequest>();

            var response=await _corpService.AddAsync(request,cancellationToken);

            return Response<AddCorpResponse>.Create(response);
        }

        /// <summary>
        /// 编辑企业
        /// </summary>
        [HttpPost("edit")]
        public async Task<Response<object>> EditAsync(CancellationToken cancellationToken)
        {
            var request = await this.GetRequestAsync<UpdateCorpRequest>();

            await _corpService.UpdateAsync(request,cancellationToken);

            return Response<object>.Create(0);
        }

        /// <summary>
        /// 获取企业信息
        /// </summary>
        public async Task<Response<GetCorpResponse>> GetAsync(CancellationToken cancellationToken)
        {
            var request = await this.GetRequestAsync<GetCorpRequest>();
            var corp=await _corpService.GetAsync(request.id,cancellationToken);
            if(corp==null)
            {
                throw new CodeException(404, _localizer["NotFound"]);
            }

            return Response<GetCorpResponse>.Create(new GetCorpResponse() { item = corp });
        }

        /// <summary>
        /// 删除企业
        /// </summary>
        [HttpPost("delete")]
        public async Task<Response<object>> DeleteAsync(CancellationToken cancellationToken)
        {
            var request = await this.GetRequestAsync<DeleteCorpsRequest>();
            var rsp = await _corpService.DeleteAsync(cancellationToken,request.ids);
            return Response<object>.Create(0);
        }
    }
}
