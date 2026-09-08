using FreeSql.DataAnnotations;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Admin.Models
{
    /// <summary>
    /// 帐号
    /// </summary>
    [Table(Name = "account")]
    [Index("{tablename}_idx_1", "name ASC", IsUnique = true)]
    [Index("{tablename}_idx_2", "created_at ASC")]
    public class Account
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        [Column(IsPrimary = true)]
        public required Guid id { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        [Column(StringLength = 50, IsNullable = false)]
        public required string name { get; set; }

        /// <summary>
        /// 昵称
        /// </summary>
        [Column(StringLength = 50, IsNullable = false)]
        public required string nickname { get; set; }

        /// <summary>
        /// 密码
        /// </summary>
        [Column(StringLength = 100, IsNullable = false)]
        public required string password { get; set; }

        /// <summary>
        /// 头像地址
        /// </summary>
        [Column(StringLength =500, IsNullable = true)]
        public string? avatar { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public required bool active { get; set; }

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
