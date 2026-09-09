using Admin.Models;
using System.Security.Cryptography;
using System.Text;

namespace Admin.Services
{
    /// <summary>
    /// 加密服务类，提供密码签名和请求签名功能
    /// </summary>
    public class EncryptorService
    {
        /// <summary>
        /// 用于内部签名的公共Key（配置项 Security:SignKey）
        /// </summary>
        private readonly string SignKey;

        /// <summary>
        /// 用于客户端侧签名密码的Key（配置项 Security:ClientPwdKey）
        /// </summary>
        private readonly string ClientPwdKey;

        /// <summary>
        /// 用于服务端侧签名密码的Key（配置项 Security:ServerPwdKey）
        /// </summary>
        private readonly string ServerPwdKey;

        /// <summary>
        /// 用于前端请求签名的公共Key（配置项 Security:RequestKey）
        /// </summary>
        private readonly string RequestKey;

        /// <summary>
        /// 各密钥从 appsettings.json 的 Security 节读取，构造时统一校验
        /// </summary>
        public EncryptorService(string? signKey, string? clientPwdKey, string? serverPwdKey, string? requestKey)
        {
            SignKey = RequireKey(signKey, "Security:SignKey");
            ClientPwdKey = RequireKey(clientPwdKey, "Security:ClientPwdKey");
            ServerPwdKey = RequireKey(serverPwdKey, "Security:ServerPwdKey");
            RequestKey = RequireKey(requestKey, "Security:RequestKey");
        }

        private static string RequireKey(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"加密服务初始化失败: 缺少配置项 {name}");
            return value;
        }



        /// <summary>
        /// 服务端侧签名密码
        /// </summary>
        /// <param name="password">已被客户侧签名过的密钥</param>
        /// <returns></returns>
        public string EncodePasswordServer(string password)
        {
            return Sign(password,ServerPwdKey);
        }

        /// <summary>
        /// 客户端侧签名密码
        /// </summary>
        /// <param name="password">密码明文</param>
        /// <returns></returns>
        public string EncodePasswordClient(string password)
        {
            return Sign(password, ClientPwdKey);
        }

        /// <summary>
        /// 从密码明文直接签名为服务端侧密钥
        /// </summary>
        /// <param name="password">密码明文</param>
        /// <returns></returns>
        public string EncodePassword(string password)
        {
            string s = EncodePasswordClient(password);
            return EncodePasswordServer(s);
        }


        /// <summary>
        /// 使用公共Key签名
        /// </summary>
        /// <param name="s">明文字符串</param>
        /// <returns></returns>
        public string Sign(string s)
        {
            return Sign(s,SignKey);
        }

        /// <summary>
        /// 使用指定Key进行签名
        /// </summary>
        /// <param name="s">明文字符串</param>
        /// <param name="key">签名密钥</param>
        /// <returns></returns>
        public static string Sign(string s,string key)
        {
            HMACSHA256 sha = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            byte[] ret = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            return BitConverter.ToString(ret).Replace("-", "").ToLower();
        }



        public string SignSystemOption(SystemOption opt)
        {
            string s = $"{opt.name}={opt.value}";
            return Sign(s);
        }


        public string SignRequestBody(string body,long ts)
        {
            string s = $"{body}{ts}";
            return Sign(s,RequestKey);
        }
    }
}
