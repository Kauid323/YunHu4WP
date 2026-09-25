using System;

namespace 云湖WP.Api.Common
{
    /// <summary>
    /// API 基础响应结果
    /// </summary>
    public class ApiResult
    {
        public int Code { get; set; }
        public string Msg { get; set; }
        public bool IsSuccess { get { return Code == 1 || Code == 0 || Code == 200; } }
    }

    /// <summary>
    /// API 泛型响应结果
    /// </summary>
    public class ApiResult<T> : ApiResult
    {
        public T Data { get; set; }
    }
}
