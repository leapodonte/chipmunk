using Admin.Services;
using System.ComponentModel.DataAnnotations;

namespace Admin.Services.DTO
{
    public class Request
    {
        public void Validate()
        {
            var context = new ValidationContext(this, null, null);
            
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(this, context, results, true);
            foreach (var result in results)
            {
                throw new CodeException(400,result.ErrorMessage);
            }

        }

    }
}
