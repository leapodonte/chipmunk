using FreeSql.DataAnnotations;
using Newtonsoft.Json.Linq;

namespace Admin.Models
{
    /// <summary>
    /// 角色拥有的模块
    /// </summary>
    [Table(Name ="role_module")]
    [Index("{tablename}_idx_1", "role_id ASC")]
    [Index("{tablename}_idx_2", "role_id ASC,module_id ASC", IsUnique = true)]
    public class RoleModule
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        [Column(IsPrimary = true)]
        public required Guid id { get; set; }

        /// <summary>
        /// 角色ID
        /// </summary>
        public required Guid role_id {  get; set; }

        /// <summary>
        /// 模块ID
        /// </summary>
        [Column(StringLength = 50, IsNullable = false)]
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
