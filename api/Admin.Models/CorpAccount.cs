using FreeSql.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Admin.Models
{
    /// <summary>
    /// 企业内身份
    /// </summary>
    public enum CorpRole
    {
        /// <summary>
        /// 默认
        /// </summary>
        Default=0,

        /// <summary>
        /// 管理员
        /// </summary>
        Admin=1,
    }

    /// <summary>
    /// 部门内身份
    /// </summary>
    public enum CorpDepartmentRole
    {
        /// <summary>
        /// 默认
        /// </summary>
        Default = 0,

        /// <summary>
        /// 主管
        /// </summary>
        Manager = 1,
    }

    /// <summary>
    /// 企业员工
    /// </summary>
    [Table(Name = "corp_account")]
    [Index("{tablename}_idx_1", "account_id ASC,corp_id ASC,department_id ASC", IsUnique = true)]
    public class CorpAccount
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        [Column(IsPrimary = true)]
        public required Guid id { get; set; }

        /// <summary>
        /// 帐号ID
        /// </summary>
        public required Guid account_id { get; set; }

        /// <summary>
        /// 所属企业ID
        /// </summary>
        public required Guid corp_id { get; set; }

        /// <summary>
        /// 所属部门ID
        /// </summary>
        public required Guid? department_id { get; set; }

        /// <summary>
        /// 企业内名称
        /// </summary>
        [Column(StringLength = 50, IsNullable = false)]
        public string? nickname { get; set; }

        /// <summary>
        /// 企业内头像
        /// </summary>
        [Column(StringLength = 500, IsNullable = false)]
        public string? avatar { get; set; }

        /// <summary>
        /// 企业内角色
        /// </summary>
        public required CorpRole corp_role { get; set; } = CorpRole.Default;

        /// <summary>
        /// 部门内角色
        /// </summary>
        public required CorpDepartmentRole department_role { get; set; } = CorpDepartmentRole.Default;

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
