namespace Admin.Services.DTO
{
    public class Response<TData>
    {
        public int code { get; set; }
        public string? msg { get; set; }
        public TData? data { get; set; }

        private Response() { }

        public static Response<TData> Create(TData? data)
        {
            var rsp = new Response<TData>();
            rsp.code = 0;
            rsp.msg = null;
            rsp.data = data;
            return rsp;
        }


        public static Response<TData> Create(int code, string? msg=null, TData? data=default)
        {
            var rsp = new Response<TData>();
            rsp.code = code;
            rsp.msg = msg;
            rsp.data = data;
            return rsp;
        }
    }
}
