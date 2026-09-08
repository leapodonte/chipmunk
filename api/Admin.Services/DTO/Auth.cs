using Admin.Models;
using Admin.Services.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Admin.Services.DTO
{

    public class LoginRequest : Request
    {
        [StringLength(100, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public string username { get; set; }

        [StringLength(100, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public string password { get; set; }
    }

    public class LoginResponse
    {
        public required string session { get; set; }
        public bool? fcp { get; set; }
    }

    public class ChangePasswordRequest:Request
    {
        [StringLength(100, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public string old_pwd { get; set; }

        [StringLength(100, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public string new_pwd { get; set; }
    }

    public class CurrentUserModulesResponse
    {
        public required List<DtoModule>? modules { get; set; }
    }


    public class CurrentUserRolesResponse
    {
        public required List<Role>? items { get; set; }
    }


}
