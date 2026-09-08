using FreeSql.DataAnnotations;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Admin.Models
{
    /// <summary>
    /// 帐号所拥有的模块
    /// </summary>
    [Table(Name = "account_module")]
    [Index("{tablename}_idx_1", "account_id ASC")]
    [Index("{tablename}_idx_2", "account_id ASC,module_id ASC", IsUnique = true)]
    public class AccountModule
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
        /// 模块ID
        /// </summary>
        [Column(StringLength = 50,IsNullable =false)]
        public required string module_id { get; set; }

        /// <summary>
        /// 模块
        /// </summary>
        [Navigate("module_id")]
        public Module? module { get; set; }

        /// <summary>
        /// 模块权限
        /// </summary>
        [JsonMap, Column(DbType = "json", IsNullable = false)]
        public JArray actions { get; set; } = new JArray( "add", "edit", "delete" );


    }
}
