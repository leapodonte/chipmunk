using FreeRedis;
using Microsoft.Extensions.Configuration;
using RTC.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using static Admin.Services.WechatClient;

namespace Admin.Services
{
    /// <summary>
    /// 微信服务类
    /// </summary>
    public class WechatClient:IDisposable
    {
        private readonly string _appId;
        private readonly string _appSecret;
        private HttpClient? _httpClient;

        public WechatClient(string appId, string appSecret)
        {
            _appId = appId;
            _appSecret = appSecret;

            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = delegate { return true; };
            _httpClient =new HttpClient(handler);

        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _httpClient = null;
        }



        public class Jscode2SessionResponse
        {
            public string? openid { get; set; }
            public string? session_key { get; set; }
            public string? unionid { get; set; }
            public int errcode { get; set; }
            public string? errmsg { get; set; }
        }

        public async Task<Jscode2SessionResponse> Jscode2SessionAsync(string jscode, CancellationToken cancellationToken)
        {
            var pms = new List<KeyValuePair<string, string>>(4);
            pms.Add(KeyValuePair.Create("appid", _appId));
            pms.Add(KeyValuePair.Create("secret", _appSecret));
            pms.Add(KeyValuePair.Create("js_code", jscode));
            pms.Add(KeyValuePair.Create("grant_type", "authorization_code"));
            using FormUrlEncodedContent content = new FormUrlEncodedContent(pms);
            string queries = await content.ReadAsStringAsync();
            string url = $"https://api.weixin.qq.com/sns/jscode2session?{queries}";
            using var rsp=await _httpClient!.GetAsync(url,cancellationToken);
            if(!rsp.IsSuccessStatusCode)
            {
                throw new CodeException((int)rsp.StatusCode,rsp.ReasonPhrase);
            }

            var jsrsp = await rsp.Content.ReadFromJsonAsync<Jscode2SessionResponse>(cancellationToken: cancellationToken);
            if(jsrsp==null)
            {
                throw new CodeException(-1,"Parse response json failed");
            }
            if(jsrsp.errcode!=0)
            {
                throw new CodeException(jsrsp.errcode,jsrsp.errmsg);
            }

            return jsrsp;
        }

        public async Task<bool> CheckSessionKeyAsync(string accessToken,string openid,string sessionKey, CancellationToken cancellationToken)
        {
            HMACSHA256 sha = new HMACSHA256(Encoding.UTF8.GetBytes(sessionKey));
            byte[] ret = sha.ComputeHash(Encoding.UTF8.GetBytes(""));
            string signature = BitConverter.ToString(ret).Replace("-", "").ToLower();

            string url = $"https://api.weixin.qq.com/wxa/checksession?access_token={accessToken}&signature={signature}&openid={openid}&sig_method=hmac_sha256";

            using var rsp=await _httpClient.GetAsync(url, cancellationToken);
            if (!rsp.IsSuccessStatusCode)
            {
                return false;
            }

            var jrsp=await rsp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            if (jrsp == null)
            {
                return false;
            }
            int? errcode = (int?)jrsp["errcode"];
            if(errcode!=0)
            {
                return false;
            }
            return true;
        }

        public async Task<string?> ResetSessionKeyAsync(string accessToken, string openid, string sessionKey, CancellationToken cancellationToken)
        {
            HMACSHA256 sha = new HMACSHA256(Encoding.UTF8.GetBytes(sessionKey));
            byte[] ret = sha.ComputeHash(Encoding.UTF8.GetBytes(""));
            string signature = BitConverter.ToString(ret).Replace("-", "").ToLower();

            string url = $"https://api.weixin.qq.com/wxa/resetusersessionkey?access_token={accessToken}&signature={signature}&openid={openid}&sig_method=hmac_sha256";

            using var rsp = await _httpClient!.GetAsync(url, cancellationToken);
            if (!rsp.IsSuccessStatusCode)
            {
                return null;
            }

            var jrsp = await rsp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            if (jrsp == null)
            {
                return null;
            }
            int? errcode = (int?)jrsp["errcode"];
            if (errcode != 0)
            {
                return null;
            }
            return (string?)jrsp["session_key"];
        }

        public class GetAccessTokenResponse
        {
            public string access_token {  get; set; }
            public int expires_in { get; set; }
        }
        public async Task<GetAccessTokenResponse> GetAccessTokenAsync(CancellationToken cancellationToken,bool forceRefresh=false)
        {
            const string url = "https://api.weixin.qq.com/cgi-bin/stable_token";
            using var content = JsonContent.Create(new
            {
                grant_type = "client_credential",
                appid = _appId,
                secret = _appSecret,
                force_refresh = forceRefresh,
            });

            using var rsp=await _httpClient!.PostAsync(url, content, cancellationToken);
            if (!rsp.IsSuccessStatusCode)
            {
                throw new CodeException((int)rsp.StatusCode, rsp.ReasonPhrase);
            }

            var jrsp = await rsp.Content.ReadFromJsonAsync<GetAccessTokenResponse>(cancellationToken: cancellationToken);
            if (jrsp == null)
            {
                throw new CodeException(-1,"Parse response json failed");
            }
            return jrsp;
        }
    }


    public class WechatService
    {
        private const string KEY_AccessToken = "app:wx:accesstoken:";
        private readonly RedisClient _redis;
        private readonly IConfiguration _cfg;

        private readonly string _appId;
        private readonly string _appSecret;

        public WechatService(IConfiguration cfg,RedisClient redis) 
        {
            _cfg = cfg;
            _redis = redis;

            _appId = _cfg["Wechat:MP:AppId"];
            _appSecret = _cfg["Wechat:MP:AppSecret"];
        }

        public async Task<string?> GetAccessTokenAsync(string userId, CancellationToken cancellationToken)
        {
            string key = KEY_AccessToken + userId;
            string accessToken=await _redis.GetAsync(key);
            if(!string.IsNullOrEmpty(accessToken))
            {
                return accessToken;
            }

            using WechatClient client = new WechatClient(_appId, _appSecret);
            var rsp = await client.GetAccessTokenAsync(cancellationToken,false);
            
            int expires = Math.Max(0, rsp.expires_in - 300);
            await _redis.SetAsync(key, rsp.access_token, expires);
            return rsp.access_token;
        }

        public async Task<Jscode2SessionResponse> Jscode2SessionAsync(string jscode, CancellationToken cancellationToken)
        {
            using WechatClient client = new WechatClient(_appId, _appSecret);
            return await client.Jscode2SessionAsync(jscode,cancellationToken);
        }
    }
}
