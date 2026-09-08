using Admin.Models;
using Admin.Services.DTO;
using Admin.Services.Properties;
using Microsoft.Extensions.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Admin.Services
{
    /// <summary>
    /// 企业服务类
    /// </summary>
    public class CorpService
    {
        private IFreeSql _fsql;
        private readonly IStringLocalizer<Resources> _localizer;

        public CorpService(IFreeSql fsql, IStringLocalizer<Resources> localizer)
        {
            _fsql = fsql;
            _localizer = localizer;
        }

        /// <summary>
        /// 查询企业列表
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ListCorpResponse> ListAsync(ListCorpRequest request, CancellationToken cancellationToken)
        {
            var q = _fsql.Select<Corp>();
            if (!string.IsNullOrWhiteSpace(request.search))
            {
                Guid id;
                if (Guid.TryParse(request.search, out id))
                {
                    q = q.Where(a => a.id == id);
                }
                else
                {
                    q = q.Where(a => a.name.StartsWith(request.search));
                }
            }
            q.OrderByDescending(a => a.created_at);
            long total = 0;
            q = q.Count(out total);
            q = q.Page(request.page, request.page_size);

            return new ListCorpResponse()
            {
                total=(ulong)total,
                items=await q.ToListAsync(cancellationToken),
            };
        }

        /// <summary>
        /// 添加企业
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="CodeException"></exception>
        public async Task<AddCorpResponse> AddAsync(AddCorpRequest request, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var corp = new Corp()
            {
                id = Guid.NewGuid(),
                name = request.name,
                type = request.type,
                created_at = now,
                updated_at = now,
                deleted_at = 0,
            };

            var r = await _fsql.Insert<Corp>(corp).ExecuteAffrowsAsync(cancellationToken);
            if (r <= 0)
            {
                throw new CodeException(500, _localizer["InternalServerError"]);
            }

            return new AddCorpResponse() { item = corp };
        }

        /// <summary>
        /// 更新企业
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task UpdateAsync(UpdateCorpRequest request, CancellationToken cancellationToken)
        {
            var q = _fsql.Update<Corp>();

            if(request.name!=null)
            {
                q = q.Set(a => a.name, request.name);
            }
            if(request.type!=null)
            {
                q = q.Set(a => a.type, request.type);
            }
            q = q.Set(a => a.updated_at, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            
            q = q.Where(a => a.id == request.id);
            await q.ExecuteAffrowsAsync(cancellationToken);
        }

        /// <summary>
        /// 删除企业
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <param name="ids"></param>
        /// <returns></returns>
        public async Task<int> DeleteAsync(CancellationToken cancellationToken, params Guid[] ids)
        {
            return await _fsql.Delete<Corp>(ids).ExecuteAffrowsAsync(cancellationToken);
        }

        /// <summary>
        /// 获取企业详情
        /// </summary>
        /// <param name="id"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<Corp?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            return await _fsql.Select<Corp>().Where(a => a.id == id).FirstAsync(cancellationToken);
        }
    }
}
