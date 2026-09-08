using FreeSql.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Admin.Models
{
    /// <summary>
    /// 角色
    /// </summary>
    [Table(Name ="role")]
    public class Role
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        [Column(IsPrimary =true)]
        public required Guid id { get;set; }

        /// <summary>
        /// 角色名称
        /// </summary>
        [Column(StringLength =50,IsNullable =false)]
        public required string name { get; set; }

        /// <summary>
        /// 是否可见
        /// </summary>
        public required bool visible { get; set; }

    }
}
