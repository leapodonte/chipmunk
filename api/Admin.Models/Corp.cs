using FreeSql.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Admin.Models
{
    /// <summary>
    /// 企业类型
    /// </summary>
    public enum CorpType
    {
        /// <summary>
        /// 默认
        /// </summary>
        Default=0,

    }

    /// <summary>
    /// 企业
    /// </summary>
    [Table(Name = "corp")]
    [Index("{tablename}_idx_1", "created_at ASC")]
    public class Corp
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        [Column(IsPrimary = true)]
        public required Guid id { get; set; }

        /// <summary>
        /// 企业名称
        /// </summary>
        [Column(StringLength = 50, IsNullable = false)]
        public required string name { get; set; }

        /// <summary>
        /// 企业类型
        /// </summary>
        public required CorpType type { get; set; } = CorpType.Default;

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
