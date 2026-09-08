using RTC.Services;

namespace Admin.Api.Services
{
    public class AdminLogService:LogService
    {
        public AdminLogService():base("admin")
        {
        }
    }
}
