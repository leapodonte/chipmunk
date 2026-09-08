using FreeSql.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Admin.Models
{
    /// <summary>
    /// 帐号所拥有的角色
    /// </summary>
    [Table(Name = "account_role")]
    [Index("{tablename}_idx_1", "account_id ASC")]
    [Index("{tablename}_idx_2", "account_id ASC,role_id ASC", IsUnique = true)]
    public class AccountRole
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
        /// 角色ID
        /// </summary>
        public required Guid role_id { get; set; }

        /// <summary>
        /// 角色
        /// </summary>
        [Navigate(nameof(role_id))]
        public Role? role { get; set; }


    }
}
