using Admin.Services.Properties;
using System.ComponentModel.DataAnnotations;
using System.Security.Principal;

namespace Admin.Services.DTO
{
    public class DtoAccount
    {
        public Guid? id { get; set; }
        public string? name { get; set; }
        public string? nickname { get; set; }
        public string? avatar { get; set; }
        public bool? active { get; set; }
        public long created_at { get; set; }
        public long updated_at { get; set; }
        public DtoAccount()
        {

        }
        public DtoAccount(Models.Account account)
        {
            id = account.id;
            name = account.name;
            nickname = account.nickname;
            avatar = account.avatar;
            active = account.active;
            created_at = account.created_at;
            updated_at = account.updated_at;
        }
    }



    public class ListAccountRequest : Request
    {
        [StringLength(100, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        public string? search { get; set; }

        public int page { get; set; } = 1;
        public int page_size { get; set; } = 20;
    }

    public class ListAccountResponse
    {
        public required ulong? total { get; set; }
        public required List<DtoAccount>? items { get; set; }
    }

    public class AddAccountRequest : Request
    {
        [StringLength(50, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public string name { get; set; }

        [StringLength(100, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public string password { get; set; }

        [StringLength(50, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public string nickname { get; set; }

        [StringLength(500, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        public string? avatar { get; set; }

        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public bool active { get; set; }
    }

    public class AddAccountResponse
    {
        public required DtoAccount item { get; set; }
    }

    public class UpdateAccountRequest : Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Guid id { get; set; }

        [StringLength(50, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        public string? name { get; set; }

        [StringLength(100, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        public string? password { get; set; }

        [StringLength(50, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        public string? nickname { get; set; }

        [StringLength(500, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        public string? avatar { get; set; }

        public bool? active { get; set; }
    }


    public class GetAccountRequest : Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Guid id { get; set; }
    }

    public class GetAccountResponse
    {
        public required DtoAccount item { get; set; }
    }

    public class DeleteAccountsRequest : Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Guid[] ids { get; set; }
    }

}
