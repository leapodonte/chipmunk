using Admin.Api.Filters;
using Admin.Models;
using Admin.Services;
using Admin.Services.DTO;
using Microsoft.AspNetCore.Mvc;

namespace Admin.Api.Controllers.Admin
{
    /// <summary>
    /// 后台管理员帐号
    /// </summary>
    [ApiController, AdminResponse, AdminCheck]
    [Route("admin/[controller]")]
    public class AccountController : ControllerBase
    {
        private readonly AccountService _accountService;

        public AccountController(AccountService accountService)
        {
            _accountService = accountService;
        }

        /// <summary>
        /// 获取帐号列表
        /// </summary>
        [HttpPost("list")]
        public async Task<Response<ListAccountResponse>> ListAsync(CancellationToken cancellationToken)
        {
            var request = await this.GetRequestAsync<ListAccountRequest>();
            ListAccountResponse response = await _accountService.ListAsync(request, cancellationToken);
            return Response<ListAccountResponse>.Create(response);
        }

        /// <summary>
        /// 添加帐号
        /// </summary>
        [HttpPost("add")]
        public async Task<Response<AddAccountResponse>> AddAsync(CancellationToken cancellationToken)
        {
            var request = await this.GetRequestAsync<AddAccountRequest>();
            
            var response = await _accountService.AddAsync(request, cancellationToken);
            return Response<AddAccountResponse>.Create(response);
        }

        /// <summary>
        /// 编辑帐号
        /// </summary>
        [HttpPost("edit")]
        public async Task<Response<object>> EditAsync(CancellationToken cancellationToken)
        {
            var request = await this.GetRequestAsync<UpdateAccountRequest>();

            await _accountService.UpdateAsync(request, cancellationToken);
            return Response<object>.Create(0);
        }

        /// <summary>
        /// 获取帐号信息
        /// </summary>
        [HttpPost("get")]
        public async Task<Response<GetAccountResponse>> GetAsync(CancellationToken cancellationToken)
        {
            var request = await this.GetRequestAsync<GetAccountRequest>();

            var response = await _accountService.GetAsync(request, cancellationToken);
            return Response<GetAccountResponse>.Create(response);
        }

        /// <summary>
        /// 删除帐号
        /// </summary>
        [HttpPost("delete")]
        public async Task<Response<object>> DeleteAsync(CancellationToken cancellationToken)
        {
            var request = await this.GetRequestAsync<DeleteAccountsRequest>();
            await _accountService.DeleteAsync(cancellationToken,request.ids);

            return Response<object>.Create(0);
        }
    }
}
