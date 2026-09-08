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
    public class ListCorpRequest : Request
    {
        [StringLength(100, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        public string? search { get; set; }

        public int page { get; set; } = 1;
        public int page_size { get; set; } = 20;
    }

    public class ListCorpResponse
    {
        public required ulong? total { get; set; }
        public required List<Corp>? items { get; set; }
    }

    public class AddCorpRequest : Request
    {
        [StringLength(50, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public string name { get; set; }

        public CorpType type { get; set; } = CorpType.Default;
    }

    public class AddCorpResponse
    {
        public required Corp item { get; set; }
    }

    public class UpdateCorpRequest : Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Guid id { get; set; }

        [StringLength(50, ErrorMessageResourceName = "Valid_StringLength", ErrorMessageResourceType = typeof(Resources))]
        public string? name { get; set; }

        public CorpType? type { get; set; } = CorpType.Default;
    }

    public class DeleteCorpsRequest : Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Guid[] ids { get; set; }
    }

    public class GetCorpRequest:Request
    {
        [Required(ErrorMessageResourceName = "Valid_CannotEmpty", ErrorMessageResourceType = typeof(Resources))]
        public Guid id { get; set; }
    }

    public class GetCorpResponse
    {
        public required Corp item { get; set; }
    }
}
