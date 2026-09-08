using Admin.Models;
using Admin.Services.DTO;
using Admin.Services.Properties;
using Microsoft.Extensions.Localization;

namespace Admin.Services
{
    /// <summary>
    /// 后台管理员帐号服务
    /// </summary>
    public class AccountService
    {
        private IFreeSql _fsql;
        private EncryptorService _encryptor;
        private readonly IStringLocalizer<Resources> _localizer;

        public AccountService(IFreeSql fsql, EncryptorService encryptor, IStringLocalizer<Resources> localizer)
        {
            _fsql = fsql;
            _encryptor = encryptor;
            _localizer = localizer;
        }

        /// <summary>
        /// 登录
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="CodeException"></exception>
        public async Task<Account> LoginAsync(LoginRequest request,CancellationToken cancellationToken)
        {
            var acc=await _fsql.Select<Models.Account>()
                .Where(a => a.name == request.username)
                .ToOneAsync(cancellationToken);
            if(acc==null)
            {
                throw new CodeException(404, _localizer["LoginFailed"]);
            }

            if(!acc.active)
            {
                throw new CodeException(405, _localizer["AccountStateError"]);
            }

            string pwd= _encryptor.EncodePasswordServer(request.password);
            if (acc.password != pwd)
            {
                throw new CodeException(404, _localizer["LoginFailed"]);
            }

            await _fsql.Update<Models.Account>()
                .Set(a => a.updated_at, DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                .ExecuteAffrowsAsync(cancellationToken);


            return acc;
        }

        /// <summary>
        /// 获取管理员帐号列表
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ListAccountResponse> ListAsync(ListAccountRequest request, CancellationToken cancellationToken)
        {
            var q = _fsql.Select<Account>();
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
            q = q.Page(request.page,request.page_size);
            var items=await q.ToListAsync(cancellationToken);
            var response = new ListAccountResponse()
            {
                total = (ulong)total,
                items = new List<DtoAccount>(),
            };

            foreach (var item in items)
            {
                response.items.Add(new DtoAccount(item));
            }
            return response;
        }

        /// <summary>
        /// 添加管理员帐号
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="CodeException"></exception>
        public async Task<AddAccountResponse> AddAsync(AddAccountRequest request, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var acc = new Account()
            {
                id = Guid.NewGuid(),
                name = request.name,
                nickname = request.nickname,
                password = _encryptor.EncodePasswordServer(request.password),
                active=request.active,
                created_at = now,
                updated_at = now,
                deleted_at = 0,
            };
            
            var r = await _fsql.Insert<Account>(acc).ExecuteAffrowsAsync(cancellationToken);
            if (r <= 0)
            {
                throw new CodeException(500, _localizer["InternalServerError"]);
            }

            return new AddAccountResponse() { item = new DtoAccount(acc) };
        }

        /// <summary>
        /// 获取管理员帐号信息
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="CodeException"></exception>
        public async Task<GetAccountResponse> GetAsync(GetAccountRequest request, CancellationToken cancellationToken)
        {
            var item = await _fsql.Select<Account>().Where(a => a.id == request.id).ToOneAsync(cancellationToken);
            if(item==null)
            {
                throw new CodeException(404, _localizer["NotFound"]);
            }
            return new GetAccountResponse() { item=new DtoAccount(item) };
        }

        /// <summary>
        /// 更新管理员帐号信息
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task UpdateAsync(UpdateAccountRequest request, CancellationToken cancellationToken)
        {
            var q = _fsql.Update<Account>();

            if (request.id == Guid.Empty)
                request.active = true; //无法禁用系统管理员

            if(request.name!=null)
            {
                q = q.Set(a => a.name, request.name);
            }
            if (request.nickname != null)
            {
                q = q.Set(a => a.nickname, request.nickname);
            }
            if (!string.IsNullOrEmpty(request.password))
            {
                string password = _encryptor.EncodePasswordServer(request.password);
                q = q.Set(a => a.password, password);
            }
            if (request.avatar != null)
            {
                q=q.Set(a => a.avatar, request.avatar);
            }
            if (request.active != null)
            {
                q=q.Set(a => a.active, request.active);
            }

            q = q.Where(a => a.id == request.id);
            await q.ExecuteAffrowsAsync(cancellationToken);
        }

        /// <summary>
        /// 删除管理员帐号
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <param name="accIds"></param>
        /// <returns></returns>
        public async Task<int> DeleteAsync(CancellationToken cancellationToken, params Guid[] accIds)
        {
            List<Guid> ids = new List<Guid>(accIds.Length);
            foreach (var id in accIds)
            {
                if (id == Guid.Empty)
                    continue;

                ids.Add(id);
            }
            return await _fsql.Delete<Account>(ids).ExecuteAffrowsAsync(cancellationToken);
        }
    }
}
