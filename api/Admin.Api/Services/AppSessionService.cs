using Admin.Models;
using Admin.Services;
using Admin.Services.DTO;
using Admin.Services.Properties;
using FreeRedis;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Text.Json;
using static System.Net.WebRequestMethods;

namespace Admin.Api.Services
{
    /// <summary>
    /// 应用会话服务
    /// </summary>
    public class AppSessionService
    {
        public const string KEY = "app:sessions";
        public readonly TimeSpan TS = TimeSpan.FromMinutes(60);
        private readonly RedisClient _redis;


        public AppSessionService(RedisClient redis)
        {
            _redis = redis;
        }
        
        public bool SetSession(AppSession session)
        {
            string key = $"{KEY}:{session.id}";
            string val=JsonSerializer.Serialize(session);
            return _redis.Set(key,val, TS,false,false,false,false)=="OK";
        }

        public bool CheckSession(string id)
        {
            string key = $"{KEY}:{id}";
            
            return _redis.Expire(key, TS);
        }

        public AppSession? GetSession(string? id)
        {
            if(id == null)
                return null;
            string key = $"{KEY}:{id}";

            if(!_redis.Expire(key, TS))
            {
                return null;
            }

            string? val=_redis.Get(key);
            if(string.IsNullOrWhiteSpace(val))
            {
                return null;
            }
            
            return JsonSerializer.Deserialize<AppSession>(val);
        }

        public bool RemoveSession(string? id)
        {
            if (id == null)
                return false;

            string key = $"{KEY}:{id}";
            return _redis.Del(key)>0;
        }

    }
}
