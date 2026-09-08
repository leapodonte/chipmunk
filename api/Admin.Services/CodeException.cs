using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Admin.Services
{
    /// <summary>
    /// 自定义异常类，包含错误码和错误信息
    /// </summary>
    public class CodeException : Exception
    {
        public int Code { get; set; }

        public CodeException(int code, string message) :
            base(message)
        {
            Code = code;
        }

        public CodeException(int code, string message, Exception innerException) :
            base(message, innerException)
        {
            Code = code;
        }
    }
}
