using Admin.Models;
using Admin.Services.Properties;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;

namespace Admin.Services.DTO
{
    public class DtoModule
    {
        public string? id { get; set; }
        public string? parent_id { get; set; }
        public string? name { get; set; }
        public int? order { get; set; }
        public string? icon { get; set; }
        public string? path { get; set; }
        public string? redirect { get; set; }
        public string? component { get; set; }
        public HashSet<string>? actions { get; set; }
        public List<DtoModule>? children { get; set; }

        public DtoModule()
        {

        }
        public DtoModule(Module mod)
        {
            id = mod.id;
            parent_id = mod.parent_id;
            name = mod.name;
            order = mod.order;
            path = mod.path;
        }

        public void MergeActions(HashSet<string>? actions)
        {
            if (this.actions == null)
            {
                this.actions = new HashSet<string>();
            }

            if (actions != null)
            {
                foreach (var action in actions)
                {
                    this.actions.Add(action);
                }
            }
        }

        public void MergeActions(JArray actions)
        {
            if (this.actions == null)
            {
                this.actions = new HashSet<string>();
            }

            if (actions != null)
            {
                foreach (string? action in actions)
                {
                    if (action != null)
                    {
                        this.actions?.Add(action);
                    }
                }
            }
        }
    }

    public class ModuleReorderRequest : Request
    {
        [Required(ErrorMessageResourceName = "CannoValid_CannotEmptytEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Dictionary<string, int> orders { get; set; }
    }

    public class ModuleUpdateRequest : Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public List<Module> modules { get; set; }
    }

    public class CombinedModulesRequest : Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Guid acc_id { get; set; }
    }

    public class CombinedModulesResponse
    {
        public required List<DtoModule>? modules { get; set; }
    }

    public class AccountModulesRequest:Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Guid acc_id { get; set; }
    }

    public class AccountModulesResponse
    {
        public required List<DtoModule>? modules { get; set; }
    }

    public class SetAccountModulesRequest:Request
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
        public Guid acc_id { get; set; }

        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Item[] items { get; set; }
    }
}
