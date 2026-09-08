
using Admin.Models;
using Admin.Services.Properties;
using System.ComponentModel.DataAnnotations;

namespace Admin.Services.DTO
{
    public class ListSystemOptionRequest:Request
    {
        public string? search { get; set; }
    }

    public class ListSystemOptionResponse
    {
        public required List<SystemOption> items { get; set; }
    }

    public class AddSystemOptionRequest : Request
    {
        [StringLength(100, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public string name { get; set; }

        [StringLength(1000, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public string value { get; set; }

        [StringLength(1000, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public string desc { get; set; }
    }

    public class AddSystemOptionResponse
    {
        public required SystemOption item { get; set; }
    }

    public class UpdateSystemOptionRequest:Request
    {
        [StringLength(100, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public string name { get; set; }

        [StringLength(1000, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        public string? value { get; set; }

        [StringLength(1000, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        public string? desc { get; set; }
    }

    public class UpdateSystemOptionResponse
    {
        public required SystemOption item { get; set; }
    }

    public class DeleteSystemOptionsRequest : Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public string[] names { get; set; }
    }
}
