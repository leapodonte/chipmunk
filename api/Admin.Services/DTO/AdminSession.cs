
namespace Admin.Services.DTO
{
    public class AdminSession
    {
        public string? id { get; set; }
        public DtoAccount? account { get; set; }
        public List<DtoModule>? modules { get; set; }
        public bool? firs_changed_pwd { get; set; }
    }
}
