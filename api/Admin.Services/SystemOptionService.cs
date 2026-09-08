using Admin.Models;
using Admin.Services.DTO;
using Admin.Services.Properties;
using Microsoft.Extensions.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Admin.Services
{
    /// <summary>
    /// 系统配置服务类
    /// </summary>
    public class SystemOptionService
    {
        private readonly IFreeSql _fsql;
        private readonly EncryptorService _encryptor;
        private readonly IStringLocalizer<Resources> _localizer;

        public SystemOptionService(IFreeSql fsql, EncryptorService encryptor, IStringLocalizer<Resources> localizer)
        {
            _fsql = fsql;
            _localizer = localizer;
            _encryptor = encryptor;
        }

        /// <summary>
        /// 获取系统配置项
        /// </summary>
        /// <param name="name"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<SystemOption?> GetAsync(string name, CancellationToken cancellationToken)
        {
            return await _fsql.Select<SystemOption>().Where(a=>a.name==name).FirstAsync(cancellationToken);
        }

        /// <summary>
        /// 设置系统配置项
        /// </summary>
        /// <param name="opt"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> SetAsync(SystemOption opt, CancellationToken cancellationToken)
        {
            var c=await _fsql.InsertOrUpdate<SystemOption>()
                .SetSource(opt)
                .ExecuteAffrowsAsync(cancellationToken);
            return c > 0;
        }

        /// <summary>
        /// 删除系统配置项
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <param name="names"></param>
        /// <returns></returns>
        public async Task<bool> RemoveAsync(CancellationToken cancellationToken,params string[] names)
        {
            var c = await _fsql.Delete<SystemOption>(names)
                .ExecuteAffrowsAsync(cancellationToken);
            return c > 0;
        }

        /// <summary>
        /// 判断是否已修改密码
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> IsChangedPasswordAsync(CancellationToken cancellationToken)
        {
            var opt=await GetAsync(SystemOption.OPT_ChangedPwd,cancellationToken);
            if (opt == null)
                return false;
            if(string.IsNullOrWhiteSpace(opt.value))
                return false;

            if(opt.value.Trim()!="1")
                return false;
            return true;
        }

        /// <summary>
        /// 获取系统配置项列表
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ListSystemOptionResponse> ListAsync(ListSystemOptionRequest request, CancellationToken cancellationToken)
        {
            var q = _fsql.Select<SystemOption>()
                .Where(a => a.visible).OrderBy(a => a.name);
            if(!string.IsNullOrWhiteSpace(request.search))
            {
                q = q.Where(a => a.name.StartsWith(request.search) || (a.desc != null && a.desc.StartsWith(request.search)) || a.value.StartsWith(request.search));
            }

            return new ListSystemOptionResponse()
            {
                items = await q.ToListAsync(cancellationToken),
            };
        }

        /// <summary>
        /// 添加系统配置项
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="CodeException"></exception>
        public async Task<AddSystemOptionResponse> AddAsync(AddSystemOptionRequest request, CancellationToken cancellationToken)
        {
            var opt = new SystemOption()
            {
                name = request.name,
                value = request.value,
                desc = request.desc,
                visible = true,
            };

            var r = await _fsql.Insert(opt).ExecuteAffrowsAsync(cancellationToken);
            if (r <= 0)
            {
                throw new CodeException(509, _localizer["Add_Failed"]);
            }
            
            return new AddSystemOptionResponse() { item = opt };
        }

        /// <summary>
        /// 更新系统配置项
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="CodeException"></exception>
        public async Task<UpdateSystemOptionResponse> UpdateAsync(UpdateSystemOptionRequest request, CancellationToken cancellationToken)
        {
            var q = _fsql.Update<SystemOption>()
                .Where(a => a.name == request.name);

            if(request.value!=null)
            {
                q = q.Set(a => a.value, request.value);
            }
            if (request.desc != null) 
            {
                q=q.Set(a => a.desc, request.desc);
            }
            var r = await q.ExecuteUpdatedAsync(cancellationToken);
            
            if (r==null||r.Count==0)
            {
                throw new CodeException(404, _localizer["Edit_Failed"]);
            }

            return new UpdateSystemOptionResponse() { item = r[0] };
        }
    }
}
