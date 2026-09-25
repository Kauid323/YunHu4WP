using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Certificates;
using Windows.Security.Cryptography.Core;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Web.Http.Filters;
using Windows.Web.Http.Headers;

namespace 云湖WP.Api.Common
{
    /// <summary>
    /// 七牛云客户端直传助手 (支持 Windows Phone 8.1 选图直接上传至云湖存储桶，已处理根证书与 WinRT 内存缓冲生命周期)
    /// </summary>
    public static class QiniuUploadHelper
    {
        private const string ImageBucket = "chat68";
        private const string FileBucket = "chat68-file";
        private const string ImageBaseUrl = "https://chat-img.jwznb.com/";
        private const string FileBaseUrl = "https://chat-file.jwznb.com/";
        private const string UserAgent = "Mozilla/5.0 (Windows Phone 8.1; ARM; Trident/7.0; Touch; rv:11.0; IEMobile/11.0; NOKIA; Lumia 930) like Gecko";

        /// <summary>
        /// 创建带有忽略 WP8.1 根证书过期特性的安全 HttpClient
        /// </summary>
        private static HttpClient CreateSafeClient()
        {
            try
            {
                var filter = new HttpBaseProtocolFilter();
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Untrusted);
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.InvalidName);
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Expired);
                filter.AllowAutoRedirect = true;
                return new HttpClient(filter);
            }
            catch
            {
                return new HttpClient();
            }
        }

        /// <summary>
        /// 上传本地图片文件到七牛云并返回公网访问 URL
        /// </summary>
        public static async Task<string> UploadImageAsync(StorageFile file, string userToken)
        {
            if (file == null) throw new ArgumentNullException("file");

            // 1. 获取图片上传 Token
            string uploadToken = await GetQiniuTokenAsync("/v1/misc/qiniu-token", userToken);
            if (string.IsNullOrEmpty(uploadToken))
            {
                throw new Exception("获取七牛云图片上传凭证失败");
            }

            // 2. 以原生流方式安全读取文件字节，避免 WinRT 句柄关闭异常
            byte[] fileBytes;
            using (var stream = await file.OpenStreamForReadAsync())
            {
                using (var ms = new MemoryStream())
                {
                    await stream.CopyToAsync(ms);
                    fileBytes = ms.ToArray();
                }
            }

            if (fileBytes == null || fileBytes.Length == 0)
            {
                throw new Exception("读取图片文件为空");
            }

            string md5 = CalculateMD5(fileBytes);
            string ext = file.FileType.ToLowerInvariant().TrimStart('.');
            if (string.IsNullOrEmpty(ext)) ext = "jpg";

            string fileKey = string.Format("{0}.{1}", md5, ext);
            string mimeType = GetMimeType(ext);

            // 3. 查询七牛上传 Host
            string uploadHost = await QueryUploadHostAsync(uploadToken, ImageBucket);

            // 4. 发送 multipart/form-data 表单直传七牛云
            await DirectUploadAsync(uploadHost, uploadToken, fileKey, fileBytes, mimeType);

            return ImageBaseUrl + fileKey;
        }

        /// <summary>
        /// 从云湖服务器获取七牛上传 Token
        /// </summary>
        private static async Task<string> GetQiniuTokenAsync(string endpoint, string userToken)
        {
            string jsonStr = await HttpHelper.GetAsync(endpoint, userToken);
            if (string.IsNullOrEmpty(jsonStr)) return "";

            try
            {
                JsonObject obj;
                if (JsonObject.TryParse(jsonStr, out obj))
                {
                    if (obj.ContainsKey("data") && obj.GetNamedValue("data").ValueType == JsonValueType.Object)
                    {
                        var data = obj.GetNamedObject("data");
                        if (data.ContainsKey("token"))
                        {
                            return data.GetNamedString("token");
                        }
                    }
                }
            }
            catch { }

            return "";
        }

        /// <summary>
        /// 动态查询七牛上传 Host 接入点
        /// </summary>
        private static async Task<string> QueryUploadHostAsync(string uploadToken, string bucket)
        {
            string defaultHost = "upload-z2.qiniup.com";
            if (string.IsNullOrEmpty(uploadToken)) return defaultHost;

            try
            {
                string ak = uploadToken.Split(':')[0];
                string queryUrl = string.Format("https://api.qiniu.com/v4/query?ak={0}&bucket={1}", ak, bucket);

                using (var client = CreateSafeClient())
                {
                    client.DefaultRequestHeaders.TryAppendWithoutValidation("User-Agent", UserAgent);
                    var response = await client.GetAsync(new Uri(queryUrl));
                    if (response.Content != null)
                    {
                        string jsonStr = await response.Content.ReadAsStringAsync();
                        JsonObject obj;
                        if (JsonObject.TryParse(jsonStr, out obj))
                        {
                            if (obj.ContainsKey("hosts"))
                            {
                                var hosts = obj.GetNamedArray("hosts");
                                if (hosts.Count > 0)
                                {
                                    var firstHost = hosts.GetObjectAt(0);
                                    if (firstHost.ContainsKey("up"))
                                    {
                                        var up = firstHost.GetNamedObject("up");
                                        if (up.ContainsKey("domains"))
                                        {
                                            var domains = up.GetNamedArray("domains");
                                            if (domains.Count > 0)
                                            {
                                                return domains.GetStringAt(0);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return defaultHost;
        }

        /// <summary>
        /// 直传二进制数据到七牛云 (使用安全 Client 与 WinRT CryptographicBuffer 保证内存不提前释放)
        /// </summary>
        private static async Task DirectUploadAsync(string uploadHost, string uploadToken, string fileKey, byte[] fileBytes, string mimeType)
        {
            using (var client = CreateSafeClient())
            {
                client.DefaultRequestHeaders.TryAppendWithoutValidation("User-Agent", "QiniuDart");

                using (var form = new HttpMultipartFormDataContent())
                {
                    form.Add(new HttpStringContent(uploadToken), "token");
                    form.Add(new HttpStringContent(fileKey), "key");

                    // 使用 CryptographicBuffer.CreateFromByteArray 创建独立的 COM 内存缓冲，彻底避免 0x80000013 Object Closed 异常
                    IBuffer nativeBuffer = CryptographicBuffer.CreateFromByteArray(fileBytes);
                    var fileContent = new HttpBufferContent(nativeBuffer);
                    fileContent.Headers.ContentType = new HttpMediaTypeHeaderValue(mimeType);
                    form.Add(fileContent, "file", fileKey);

                    var uri = new Uri(string.Format("https://{0}/", uploadHost));
                    var response = await client.PostAsync(uri, form);

                    if (!response.IsSuccessStatusCode)
                    {
                        string errBody = "";
                        try { errBody = await response.Content.ReadAsStringAsync(); } catch { }
                        throw new Exception(string.Format("七牛云上传返回错误 ({0}): {1}", (int)response.StatusCode, errBody));
                    }
                }
            }
        }

        /// <summary>
        /// 计算字节数组的 MD5
        /// </summary>
        public static string CalculateMD5(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            var alg = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Md5);
            var buffer = CryptographicBuffer.CreateFromByteArray(bytes);
            var hashed = alg.HashData(buffer);
            return CryptographicBuffer.EncodeToHexString(hashed).ToLowerInvariant();
        }

        /// <summary>
        /// 获取对应 MIME 类型
        /// </summary>
        private static string GetMimeType(string ext)
        {
            switch (ext.ToLowerInvariant())
            {
                case "png": return "image/png";
                case "gif": return "image/gif";
                case "webp": return "image/webp";
                case "bmp": return "image/bmp";
                case "jpeg":
                case "jpg":
                default: return "image/jpeg";
            }
        }
    }
}
