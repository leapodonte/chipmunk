using FreeSql.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Admin.Models
{
    /// <summary>
    /// 部门类型
    /// </summary>
    public enum CorpDepartmentType
    {
        /// <summary>
        /// 默认
        /// </summary>
        Default=0,
    }

    /// <summary>
    /// 企业部门
    /// </summary>
    [Table(Name = "corp_department")]
    public class CorpDepartment
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        [Column(IsPrimary = true)]
        public required Guid id { get; set; }

        /// <summary>
        /// 所属企业ID
        /// </summary>
        public required Guid corp_id { get; set; }

        /// <summary>
        /// 上级部门ID
        /// </summary>
        [Column(IsNullable =true)]
        public Guid? parent_id { get; set; }

        /// <summary>
        /// 部门名称
        /// </summary>
        [Column(StringLength = 50, IsNullable = false)]
        public required string name { get; set; }

        /// <summary>
        /// 部门类型
        /// </summary>
        public required CorpDepartmentType type { get; set; } = CorpDepartmentType.Default;

        /// <summary>
        /// 创建时间
        /// </summary>
        public long created_at { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public long updated_at { get; set; }
    }
}
