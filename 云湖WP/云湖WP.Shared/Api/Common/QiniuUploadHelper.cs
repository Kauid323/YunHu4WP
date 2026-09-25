using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Certificates;
using Windows.Security.Cryptography.Core;
using Windows.Storage;
using Windows.Web.Http;
using Windows.Web.Http.Filters;
using Windows.Web.Http.Headers;
using 云湖WP.Utils;

namespace 云湖WP.Api.Common
{
    /// <summary>
    /// 七牛云客户端直传助手 (支持 Windows Phone 8.1 选图直接上传至云湖存储桶，含完整诊断日志与多协议通道)
    /// </summary>
    public static class QiniuUploadHelper
    {
        private const string ImageBucket = "chat68";
        private const string FileBucket = "chat68-file";
        private const string ImageBaseUrl = "https://chat-img.jwznb.com/";
        private const string FileBaseUrl = "https://chat-file.jwznb.com/";

        /// <summary>
        /// 上传本地图片文件到七牛云并返回公网访问 URL (支持上传实时进度通知)
        /// </summary>
        public static async Task<string> UploadImageAsync(StorageFile file, string userToken, IProgress<double> progress = null)
        {
            if (file == null) throw new ArgumentNullException("file");

            if (progress != null) progress.Report(5.0);
            AppLogger.Log("QiniuUpload", string.Format("开始处理本地图片: Name={0}, Path={1}", file.Name, file.Path));

            // 1. 获取图片上传 Token
            AppLogger.Log("QiniuUpload", "正在请求七牛上传凭证 /v1/misc/qiniu-token ...");
            string uploadToken = await GetQiniuTokenAsync("/v1/misc/qiniu-token", userToken);
            if (string.IsNullOrEmpty(uploadToken))
            {
                AppLogger.Log("QiniuUpload", "错误: 获取七牛云上传 Token 为空");
                throw new Exception("获取七牛云图片上传凭证失败");
            }
            if (progress != null) progress.Report(15.0);
            AppLogger.Log("QiniuUpload", "获取上传 Token 成功: " + uploadToken.Substring(0, Math.Min(16, uploadToken.Length)) + "...");

            // 2. 使用安全 Stream 流式读取文件字节
            AppLogger.Log("QiniuUpload", "正在读取文件二进制流...");
            byte[] fileBytes;
            using (var stream = await file.OpenStreamForReadAsync())
            {
                fileBytes = new byte[stream.Length];
                int totalRead = 0;
                while (totalRead < fileBytes.Length)
                {
                    int chunk = await stream.ReadAsync(fileBytes, totalRead, fileBytes.Length - totalRead);
                    if (chunk <= 0) break;
                    totalRead += chunk;
                }
            }

            if (fileBytes == null || fileBytes.Length == 0)
            {
                AppLogger.Log("QiniuUpload", "错误: 读取到的文件字节为空");
                throw new Exception("读取图片文件为空");
            }

            string md5 = CalculateMD5(fileBytes);
            string ext = file.FileType.ToLowerInvariant().TrimStart('.');
            if (string.IsNullOrEmpty(ext)) ext = "jpg";

            string fileKey = string.Format("{0}.{1}", md5, ext);
            string mimeType = GetMimeType(ext);

            AppLogger.Log("QiniuUpload", string.Format("文件解析成功: Key={0}, Size={1} bytes, MIME={2}", fileKey, fileBytes.Length, mimeType));

            // 3. 查询七牛上传 Host
            string uploadHost = await QueryUploadHostAsync(uploadToken, ImageBucket);
            AppLogger.Log("QiniuUpload", "七牛目标上传 Host: " + uploadHost);
            if (progress != null) progress.Report(20.0);

            // 4. 手工封装整块 Multipart 二进制 Payload
            string boundary = "----YunhuWPBoundary" + DateTime.UtcNow.Ticks.ToString("x");
            byte[] multipartPayload = BuildMultipartPayload(boundary, uploadToken, fileKey, fileBytes, mimeType);
            AppLogger.Log("QiniuUpload", string.Format("Multipart 表单构建完成，总 Payload 长度={0} 字节", multipartPayload.Length));

            // 5. 执行直传 (带流式写入进度报告)
            await DirectUploadPayloadAsync(uploadHost, boundary, multipartPayload, progress);

            if (progress != null) progress.Report(100.0);

            string finalUrl = ImageBaseUrl + fileKey;
            AppLogger.Log("QiniuUpload", "图片直传成功！公网地址: " + finalUrl);
            return finalUrl;
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
            catch (Exception ex)
            {
                AppLogger.Log("QiniuUpload", "解析七牛 Token JSON 异常: " + ex.Message);
            }

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
                string jsonStr = await HttpHelper.GetAsync(queryUrl);

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
                                        string queried = domains.GetStringAt(0);
                                        AppLogger.Log("QiniuUpload", "查询到七牛动态接入点: " + queried);
                                        return queried;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("QiniuUpload", "查询七牛 Host 异常 (将使用默认节点): " + ex.Message);
            }

            return defaultHost;
        }

        /// <summary>
        /// 手工将表单各字段与二进制文件构造为标准的 RFC 1867 / 2388 multipart/form-data 字节流
        /// 避免 WinRT 8.1 内部 HttpMultipartFormDataContent 的 COM 流关闭与 ObjectDisposedException 缺陷
        /// </summary>
        private static byte[] BuildMultipartPayload(string boundary, string uploadToken, string fileKey, byte[] fileBytes, string mimeType)
        {
            using (var ms = new MemoryStream())
            {
                // 1. token 字段
                byte[] tokenHeader = Encoding.UTF8.GetBytes(
                    string.Format("--{0}\r\nContent-Disposition: form-data; name=\"token\"\r\n\r\n{1}\r\n", boundary, uploadToken));
                ms.Write(tokenHeader, 0, tokenHeader.Length);

                // 2. key 字段
                byte[] keyHeader = Encoding.UTF8.GetBytes(
                    string.Format("--{0}\r\nContent-Disposition: form-data; name=\"key\"\r\n\r\n{1}\r\n", boundary, fileKey));
                ms.Write(keyHeader, 0, keyHeader.Length);

                // 3. file 二进制字段头部
                byte[] fileHeader = Encoding.UTF8.GetBytes(
                    string.Format("--{0}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{1}\"\r\nContent-Type: {2}\r\n\r\n", boundary, fileKey, mimeType));
                ms.Write(fileHeader, 0, fileHeader.Length);

                // 4. file 二进制数据
                ms.Write(fileBytes, 0, fileBytes.Length);

                // 5. 尾部 Boundary
                byte[] footer = Encoding.UTF8.GetBytes(
                    string.Format("\r\n--{0}--\r\n", boundary));
                ms.Write(footer, 0, footer.Length);

                return ms.ToArray();
            }
        }

        /// <summary>
        /// 使用多通道策略执行直传 (通道 1: HttpWebRequest; 通道 2: WinRT 单块 BufferContent; 支持 HTTPS/HTTP 自动降级)
        /// </summary>
        private static async Task DirectUploadPayloadAsync(string uploadHost, string boundary, byte[] payload, IProgress<double> progress = null)
        {
            Exception lastEx = null;
            string[] hostsToTry = new string[] { uploadHost, "upload-z2.qiniup.com", "up-z2.qiniup.com" };
            string[] protos = new string[] { "http", "https" };

            // 优先使用 .NET 标准 HttpWebRequest 进行流式直传 (无 WinRT COM 句柄依赖)
            foreach (var h in hostsToTry)
            {
                foreach (var proto in protos)
                {
                    string targetUrl = string.Format("{0}://{1}/", proto, h);
                    AppLogger.Log("QiniuUpload", "尝试通道 1 (HttpWebRequest) -> " + targetUrl);

                    try
                    {
                        var request = (HttpWebRequest)WebRequest.Create(new Uri(targetUrl));
                        request.Method = "POST";
                        request.ContentType = "multipart/form-data; boundary=" + boundary;
                        request.Headers["User-Agent"] = "QiniuDart";

                        using (var reqStream = await request.GetRequestStreamAsync())
                        {
                            int chunkSize = 32768; // 32 KB
                            int sent = 0;
                            while (sent < payload.Length)
                            {
                                int count = Math.Min(chunkSize, payload.Length - sent);
                                await reqStream.WriteAsync(payload, sent, count);
                                sent += count;
                                if (progress != null)
                                {
                                    double pct = 20.0 + ((double)sent / payload.Length) * 75.0;
                                    progress.Report(pct);
                                }
                            }
                            await reqStream.FlushAsync();
                        }

                        using (var resp = (HttpWebResponse)await request.GetResponseAsync())
                        using (var respStream = resp.GetResponseStream())
                        using (var reader = new StreamReader(respStream))
                        {
                            string respText = await reader.ReadToEndAsync();
                            AppLogger.Log("QiniuUpload", string.Format("通道 1 上传成功 (HTTP {0}): {1}", (int)resp.StatusCode, respText));
                            return;
                        }
                    }
                    catch (WebException wex)
                    {
                        string errDetail = wex.Message;
                        if (wex.Response != null)
                        {
                            try
                            {
                                using (var errStream = wex.Response.GetResponseStream())
                                using (var reader = new StreamReader(errStream))
                                {
                                    errDetail = reader.ReadToEnd();
                                }
                            }
                            catch { }
                        }
                        AppLogger.Log("QiniuUpload", "通道 1 失败: " + errDetail);
                        lastEx = new Exception("七牛上传返回: " + errDetail, wex);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log("QiniuUpload", "通道 1 异常: " + ex.Message);
                        lastEx = ex;
                    }
                }
            }

            // 若通道 1 未成功，尝试通道 2: WinRT 单一 HttpBufferContent
            foreach (var h in hostsToTry)
            {
                foreach (var proto in protos)
                {
                    string targetUrl = string.Format("{0}://{1}/", proto, h);
                    AppLogger.Log("QiniuUpload", "尝试通道 2 (WinRT HttpBufferContent) -> " + targetUrl);

                    try
                    {
                        var filter = new HttpBaseProtocolFilter();
                        filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Untrusted);
                        filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.InvalidName);
                        filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Expired);
                        filter.AllowAutoRedirect = true;

                        using (var client = new HttpClient(filter))
                        {
                            client.DefaultRequestHeaders.TryAppendWithoutValidation("User-Agent", "QiniuDart");

                            var nativeBuffer = CryptographicBuffer.CreateFromByteArray(payload);
                            var content = new HttpBufferContent(nativeBuffer);
                            content.Headers.ContentType = new HttpMediaTypeHeaderValue("multipart/form-data");
                            content.Headers.ContentType.Parameters.Add(new HttpNameValueHeaderValue("boundary", boundary));

                            var resp = await client.PostAsync(new Uri(targetUrl), content);
                            if (resp.IsSuccessStatusCode)
                            {
                                string respBody = await resp.Content.ReadAsStringAsync();
                                AppLogger.Log("QiniuUpload", "通道 2 上传成功: " + respBody);
                                return;
                            }
                            else
                            {
                                string errBody = await resp.Content.ReadAsStringAsync();
                                AppLogger.Log("QiniuUpload", string.Format("通道 2 返回错误 ({0}): {1}", (int)resp.StatusCode, errBody));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log("QiniuUpload", "通道 2 异常: " + ex.Message);
                        lastEx = ex;
                    }
                }
            }

            if (lastEx != null)
            {
                throw lastEx;
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

