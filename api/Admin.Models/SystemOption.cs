using FreeSql.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Admin.Models
{
    /// <summary>
    /// 系统配置
    /// </summary>
    [Table(Name ="system_option")]
    public class SystemOption
    {
        public const string OPT_SystemID = "system.id";
        public const string OPT_ChangedPwd = "system.changed_pwd";


        [Column(IsPrimary = true,StringLength =100)]
        public required string name { get;set; }

        [Column(StringLength =1000,IsNullable =false)]
        public required string value { get;set; }

        [Column(StringLength =1000, IsNullable =true)]
        public string? desc { get;set; }

        [Column(StringLength = 100, IsNullable = true)]
        public string? signature { get;set; }

        [Column(IsNullable = false)]
        public required bool visible { get; set; }
    }
}
