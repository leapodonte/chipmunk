using FreeSql.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Admin.Models
{
    /// <summary>
    /// 用户类型
    /// </summary>
    public enum UserType
    {
        /// <summary>
        /// 客户端类型
        /// </summary>
        Client,

        /// <summary>
        /// 微信小程序
        /// </summary>
        WechatMP,
    }

    /// <summary>
    /// 用户状态
    /// </summary>
    public enum UserState
    {
        /// <summary>
        /// 启用
        /// </summary>
        Active,

        /// <summary>
        /// 禁用
        /// </summary>
        Disabled,
    }

    /// <summary>
    /// 用户帐号
    /// </summary>
    [Table(Name = "user")]
    [Index("{tablename}_idx_1", "name ASC,deleted_at ASC", IsUnique = true)]
    [Index("{tablename}_idx_2", "open_id ASC,deleted_at ASC", IsUnique = true)]
    [Index("{tablename}_idx_3", "unique_id ASC,deleted_at ASC", IsUnique = true)]
    public class User
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [Column(IsPrimary = true)]
        public Guid id { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        [Column(StringLength = 50, IsNullable = true)]
        public string? name { get; set; }

        /// <summary>
        /// 用户昵称
        /// </summary>
        [Column(StringLength = 50, IsNullable = true)]
        public string? nickname { get; set; }

        /// <summary>
        /// 用户头像
        /// </summary>
        [Column(StringLength = 500, IsNullable = true)]
        public string? avatar { get; set; }

        /// <summary>
        /// 用户类型
        /// </summary>
        public UserType type { get; set; }

        /// <summary>
        /// 用户状态
        /// </summary>
        public UserState state { get; set; }

        /// <summary>
        /// 小程序关联openid
        /// </summary>
        public string? openid { get; set; }

        /// <summary>
        /// 小程序关联unionid
        /// </summary>
        public string? unionid { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public long created_at { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public long updated_at { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public long deleted_at { get; set; }
    }
}
