using Admin.Services.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Admin.Services.DTO
{
    public class WxLoginRequest: Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public string code { get; set; }
    }

    public class WxLoginResponse
    {
        public string session_id { get; set; }
    }
}
