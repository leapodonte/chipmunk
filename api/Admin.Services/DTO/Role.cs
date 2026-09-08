using Admin.Models;
using Admin.Services.Properties;
using System.ComponentModel.DataAnnotations;

namespace Admin.Services.DTO
{
    public class ListRoleRequest:Request
    {
        [StringLength(50, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        public string? search { get; set; }

        public int page { get; set; } = 1;
        public int page_size { get; set; } = 20;
    }

    public class ListRoleResponse
    {
        public required ulong? total { get; set; }
        public required List<Role>? items { get; set; }
    }


    public class AddRoleRequest:Request
    {
        [StringLength(50, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public string name { get; set; }

        /// <summary>
        /// 是否可见
        /// </summary>
        public bool visible { get; set; } = true;
    }

    public class UpdateRoleRequest : Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Guid id { get; set; }

        [StringLength(50, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        public string name { get; set; }

        /// <summary>
        /// 是否可见
        /// </summary>
        public bool? visible { get; set; }
    }

    public class DeleteRolesRequest : Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Guid[]? ids { get; set; }
    }

    public class GetRoleRequest : Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Guid id { get; set; }
    }

    public class GetRoleResponse
    {
        public required Role item { get; set; }
    }

    public class SetRoleModulesRequest : Request
    {
        public class Item
        {
            [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
            [StringLength(50, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
            public string mod_id { get; set; }

            [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
            [StringLength(50, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
            public HashSet<string> actions { get; set; }
        }
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Guid role_id { get; set; }

        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Item[] items { get; set; }
    }

    public class GetRoleModulesRequest:Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Guid role_id { get; set; }
    }

    public class GetRoleModulesResponse
    {
        public required List<DtoModule> items { get; set; }
    }

    public class GetAccountRolesRequest:Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Guid acc_id { get; set; }
    }

    public class GetAccountRolesResponse
    {
        public required List<Role> items { get; set; }
    }

    public class SetAccountRoleRequest:Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Guid acc_id { get; set; }

        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Guid[] role_ids { get; set; }
    }
}
