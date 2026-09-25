using System;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Certificates;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Web.Http.Filters;
using Windows.Web.Http.Headers;

namespace 云湖WP.Api.Common
{
    /// <summary>
    /// 基于 WinRT HttpClient 的统一网络请求助手 (支持过时根证书校验忽略、自动网络故障重试与友好异常处理)
    /// </summary>
    public static class HttpHelper
    {
        private const int MaxRetryAttempts = 2;
        private const string UserAgent = "Mozilla/5.0 (Windows Phone 8.1; ARM; Trident/7.0; Touch; rv:11.0; IEMobile/11.0; NOKIA; Lumia 930) like Gecko";

        /// <summary>
        /// 创建并配置带有 WP8.1 根证书过期忽略特性的 HttpClient
        /// </summary>
        private static HttpClient CreateClient(string token = null, string acceptType = null)
        {
            HttpClient client;
            try
            {
                var filter = new HttpBaseProtocolFilter();
                // 忽略 Windows Phone 8.1 根证书过期导致的安全连接异常 (0x80072EFD / 0x80072F8F)
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Untrusted);
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.InvalidName);
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Expired);
                filter.AllowAutoRedirect = true;

                client = new HttpClient(filter);
            }
            catch
            {
                client = new HttpClient();
            }

            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.TryAppendWithoutValidation("token", token);
                client.DefaultRequestHeaders.TryAppendWithoutValidation("Authorization", "Bearer " + token);
            }
            if (!string.IsNullOrEmpty(acceptType))
            {
                client.DefaultRequestHeaders.TryAppendWithoutValidation("Accept", acceptType);
            }
            client.DefaultRequestHeaders.TryAppendWithoutValidation("User-Agent", UserAgent);

            return client;
        }

        /// <summary>
        /// 格式化网络异常，提供友好的错误提示 (针对 0x80072EFD、0x80072EE7、0x80072EE2 等)
        /// </summary>
        public static Exception FormatFriendlyNetworkException(Exception ex)
        {
            if (ex == null) return new Exception("网络连接失败");

            string msg = ex.Message ?? "";
            uint hresult = (uint)ex.HResult;

            if (hresult == 0x80072EFD || msg.IndexOf("0x80072EFD", StringComparison.OrdinalIgnoreCase) >= 0 || msg.IndexOf("80072efd", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Exception("无法连接到云湖服务器 (0x80072EFD)，请检查网络连接后重试", ex);
            }
            if (hresult == 0x80072EE7 || msg.IndexOf("0x80072EE7", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Exception("网络 DNS 解析失败，请检查网络设置", ex);
            }
            if (hresult == 0x80072EE2 || msg.IndexOf("0x80072EE2", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Exception("网络请求超时，请检查网络后重试", ex);
            }

            return ex;
        }

        /// <summary>
        /// 执行 GET 请求 (带自动重试)
        /// </summary>
        public static async Task<string> GetAsync(string endpoint, string token = null)
        {
            var url = endpoint.StartsWith("http") ? endpoint : YunhuApiConfig.BaseUrl + endpoint;
            var uri = new Uri(url);
            Exception lastEx = null;

            for (int attempt = 0; attempt <= MaxRetryAttempts; attempt++)
            {
                Exception currentEx = null;
                try
                {
                    using (var client = CreateClient(token))
                    {
                        var response = await client.GetAsync(uri);
                        if (response.Content != null)
                        {
                            return await response.Content.ReadAsStringAsync();
                        }
                        return "";
                    }
                }
                catch (Exception ex)
                {
                    currentEx = ex;
                    lastEx = ex;
                }

                if (currentEx != null && attempt < MaxRetryAttempts)
                {
                    await Task.Delay(200 * (attempt + 1));
                }
            }

            throw FormatFriendlyNetworkException(lastEx);
        }

        /// <summary>
        /// 执行 GET Protobuf 请求 (带自动重试)
        /// </summary>
        public static async Task<byte[]> GetProtobufAsync(string endpoint, string token = null)
        {
            var url = endpoint.StartsWith("http") ? endpoint : YunhuApiConfig.BaseUrl + endpoint;
            var uri = new Uri(url);
            Exception lastEx = null;

            for (int attempt = 0; attempt <= MaxRetryAttempts; attempt++)
            {
                Exception currentEx = null;
                try
                {
                    using (var client = CreateClient(token, "application/x-protobuf"))
                    {
                        var response = await client.GetAsync(uri);
                        if (response.Content != null)
                        {
                            var responseBuffer = await response.Content.ReadAsBufferAsync();
                            if (responseBuffer != null && responseBuffer.Length > 0)
                            {
                                byte[] bytes = new byte[responseBuffer.Length];
                                using (var reader = DataReader.FromBuffer(responseBuffer))
                                {
                                    reader.ReadBytes(bytes);
                                }
                                return bytes;
                            }
                        }
                        return new byte[0];
                    }
                }
                catch (Exception ex)
                {
                    currentEx = ex;
                    lastEx = ex;
                }

                if (currentEx != null && attempt < MaxRetryAttempts)
                {
                    await Task.Delay(200 * (attempt + 1));
                }
            }

            throw FormatFriendlyNetworkException(lastEx);
        }

        /// <summary>
        /// 执行 POST JSON 请求 (带自动重试)
        /// </summary>
        public static async Task<string> PostJsonAsync(string endpoint, string jsonBody, string token = null)
        {
            var url = endpoint.StartsWith("http") ? endpoint : YunhuApiConfig.BaseUrl + endpoint;
            var uri = new Uri(url);
            if (string.IsNullOrEmpty(jsonBody))
            {
                jsonBody = "{}";
            }
            Exception lastEx = null;

            for (int attempt = 0; attempt <= MaxRetryAttempts; attempt++)
            {
                Exception currentEx = null;
                try
                {
                    using (var client = CreateClient(token))
                    using (var content = new HttpStringContent(jsonBody, UnicodeEncoding.Utf8, "application/json"))
                    {
                        var response = await client.PostAsync(uri, content);
                        if (response.Content != null)
                        {
                            return await response.Content.ReadAsStringAsync();
                        }
                        return "";
                    }
                }
                catch (Exception ex)
                {
                    currentEx = ex;
                    lastEx = ex;
                }

                if (currentEx != null && attempt < MaxRetryAttempts)
                {
                    await Task.Delay(200 * (attempt + 1));
                }
            }

            throw FormatFriendlyNetworkException(lastEx);
        }

        /// <summary>
        /// 执行 POST Protobuf 二进制请求并返回二进制响应 (带自动重试与 SSL 根证书忽略)
        /// </summary>
        public static async Task<byte[]> PostProtobufAsync(string endpoint, byte[] requestBytes, string token = null)
        {
            var url = endpoint.StartsWith("http") ? endpoint : YunhuApiConfig.BaseUrl + endpoint;
            var uri = new Uri(url);
            Exception lastEx = null;

            for (int attempt = 0; attempt <= MaxRetryAttempts; attempt++)
            {
                Exception currentEx = null;
                try
                {
                    using (var client = CreateClient(token, "application/x-protobuf"))
                    {
                        IHttpContent content;
                        if (requestBytes != null && requestBytes.Length > 0)
                        {
                            var buffer = CryptographicBuffer.CreateFromByteArray(requestBytes);
                            var bufferContent = new HttpBufferContent(buffer);
                            HttpMediaTypeHeaderValue mediaType;
                            if (HttpMediaTypeHeaderValue.TryParse("application/x-protobuf", out mediaType))
                            {
                                bufferContent.Headers.ContentType = mediaType;
                            }
                            content = bufferContent;
                        }
                        else
                        {
                            content = new HttpStringContent("", UnicodeEncoding.Utf8, "application/x-protobuf");
                        }

                        using (content)
                        {
                            var response = await client.PostAsync(uri, content);
                            if (response.Content != null)
                            {
                                var responseBuffer = await response.Content.ReadAsBufferAsync();
                                if (responseBuffer != null && responseBuffer.Length > 0)
                                {
                                    byte[] bytes = new byte[responseBuffer.Length];
                                    using (var reader = DataReader.FromBuffer(responseBuffer))
                                    {
                                        reader.ReadBytes(bytes);
                                    }
                                    return bytes;
                                }
                            }
                            return new byte[0];
                        }
                    }
                }
                catch (Exception ex)
                {
                    currentEx = ex;
                    lastEx = ex;
                }

                if (currentEx != null && attempt < MaxRetryAttempts)
                {
                    await Task.Delay(200 * (attempt + 1));
                }
            }

            throw FormatFriendlyNetworkException(lastEx);
        }
    }
}
