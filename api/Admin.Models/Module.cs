using FreeSql.DataAnnotations;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Admin.Models
{
    /// <summary>
    /// 模块
    /// </summary>
    [Table(Name = "module")]
    public class Module
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        [Column(IsPrimary = true,StringLength =50)]
        public required string id { get; set; }

        /// <summary>
        /// 父模块ID
        /// </summary>
        [Column(StringLength =50,IsNullable =true)]
        public string? parent_id { get; set; }

        /// <summary>
        /// 名称
        /// </summary>
        [Column(StringLength = 50,IsNullable =false)]
        public required string name { get; set; }


        /// <summary>
        /// 是否可见
        /// </summary>
        public bool visible { get; set; } = true;

        /// <summary>
        /// 顺序
        /// </summary>
        public required int order { get; set; }

        /// <summary>
        /// 模块路径
        /// </summary>
        [Column(StringLength = 50,IsNullable =false)]
        public required string path { get; set; }

        /// <summary>
        /// 模块功能
        /// </summary>
        [JsonMap, Column(DbType = "jsonb", IsNullable = false)]
        public JArray actions { get; set; } = new JArray("add","edit","delete");

        /// <summary>
        /// 子模块
        /// </summary>
        [Navigate(nameof(parent_id))]
        public List<Module>? children { get; set; }
    }
}
