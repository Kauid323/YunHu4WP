using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
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
    /// 七牛云上传详细结果结构体 (包含 Key, Hash, 尺寸, 宽高, 原始文件名与扩展名)
    /// </summary>
    public class QiniuUploadResult
    {
        public string Key { get; set; }
        public string Hash { get; set; }
        public long FileSize { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string FileName { get; set; }
        public string FileExtension { get; set; }
        public string MimeType { get; set; }
        public string PublicUrl { get; set; }
    }

    /// <summary>
    /// 七牛云客户端直传助手 (支持图片、视频、音频、文件专用 Token 直传、实时进度与取消操作)
    /// </summary>
    public static class QiniuUploadHelper
    {
        private const string ImageBucket = "chat68";
        private const string VideoBucket = "chat68-video";
        private const string AudioBucket = "chat68-audio";
        private const string FileBucket = "chat68-file";

        private const string ImageBaseUrl = "https://chat-img.jwznb.com/";
        private const string VideoBaseUrl = "https://chat-video1.jwznb.com/";
        private const string AudioBaseUrl = "https://chat-audio1.jwznb.com/";
        private const string FileBaseUrl = "https://chat-file.jwznb.com/";

        private static string GetBaseUrlByBucket(string bucket)
        {
            if (string.IsNullOrEmpty(bucket)) return ImageBaseUrl;
            if (bucket.IndexOf("video", StringComparison.OrdinalIgnoreCase) >= 0) return VideoBaseUrl;
            if (bucket.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0) return AudioBaseUrl;
            if (bucket.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0 || bucket.IndexOf("disk", StringComparison.OrdinalIgnoreCase) >= 0) return FileBaseUrl;
            return ImageBaseUrl;
        }

        /// <summary>
        /// 上传本地图片文件到七牛云并返回公网访问 URL (GET /v1/misc/qiniu-token)
        /// </summary>
        public static async Task<string> UploadImageAsync(StorageFile file, string userToken, IProgress<double> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (file == null) throw new ArgumentNullException("file");
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

            if (progress != null) progress.Report(5.0);
            AppLogger.Log("QiniuUpload", string.Format("开始处理本地图片: Name={0}, Path={1}", file.Name, file.Path));

            // 1. 获取图片上传 Token (/v1/misc/qiniu-token)
            AppLogger.Log("QiniuUpload", "正在请求图片七牛上传凭证 /v1/misc/qiniu-token ...");
            string uploadToken = await GetQiniuTokenAsync("/v1/misc/qiniu-token", userToken);
            if (string.IsNullOrEmpty(uploadToken))
            {
                AppLogger.Log("QiniuUpload", "错误: 获取七牛云图片上传 Token 为空");
                throw new Exception("获取七牛云图片上传凭证失败");
            }
            if (progress != null) progress.Report(15.0);
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

            string targetBucket = ParseBucketFromToken(uploadToken);
            if (string.IsNullOrEmpty(targetBucket)) targetBucket = ImageBucket;
            AppLogger.Log("QiniuUpload", string.Format("图片上传 Token 目标 Bucket: {0}", targetBucket));

            // 2. 读取文件
            AppLogger.Log("QiniuUpload", "正在读取图片二进制流...");
            byte[] fileBytes;
            using (var stream = await file.OpenStreamForReadAsync())
            {
                fileBytes = new byte[stream.Length];
                int totalRead = 0;
                while (totalRead < fileBytes.Length)
                {
                    if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();
                    int chunk = await stream.ReadAsync(fileBytes, totalRead, fileBytes.Length - totalRead);
                    if (chunk <= 0) break;
                    totalRead += chunk;
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

            AppLogger.Log("QiniuUpload", string.Format("图片解析成功: Key={0}, Size={1} bytes, MIME={2}", fileKey, fileBytes.Length, mimeType));

            // 3. 查询七牛上传 Host
            string uploadHost = await QueryUploadHostAsync(uploadToken, targetBucket);
            AppLogger.Log("QiniuUpload", "七牛目标上传 Host: " + uploadHost);
            if (progress != null) progress.Report(20.0);
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

            // 4. 手工封装 Payload
            string boundary = "----YunhuWPBoundary" + DateTime.UtcNow.Ticks.ToString("x");
            byte[] multipartPayload = BuildMultipartPayload(boundary, uploadToken, fileKey, fileBytes, mimeType);

            // 5. 执行直传
            await DirectUploadPayloadAsync(uploadHost, boundary, multipartPayload, progress, cancellationToken);

            if (progress != null) progress.Report(100.0);

            string baseUrl = GetBaseUrlByBucket(targetBucket);
            string finalUrl = baseUrl + fileKey;
            AppLogger.Log("QiniuUpload", "图片直传成功！公网地址: " + finalUrl);
            return finalUrl;
        }

        /// <summary>
        /// 上传本地视频文件到七牛云并返回详细结构化信息 (GET /v1/misc/qiniu-token-video)
        /// </summary>
        public static async Task<QiniuUploadResult> UploadVideoDetailedAsync(StorageFile file, string userToken, IProgress<double> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (file == null) throw new ArgumentNullException("file");
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

            if (progress != null) progress.Report(5.0);
            AppLogger.Log("QiniuUpload", string.Format("开始处理本地视频: Name={0}, Path={1}", file.Name, file.Path));

            // 1. 获取视频专门的七牛上传 Token (/v1/misc/qiniu-token-video)
            AppLogger.Log("QiniuUpload", "正在请求视频七牛上传凭证 /v1/misc/qiniu-token-video ...");
            string uploadToken = await GetQiniuTokenAsync("/v1/misc/qiniu-token-video", userToken);
            if (string.IsNullOrEmpty(uploadToken))
            {
                AppLogger.Log("QiniuUpload", "错误: 获取七牛云视频上传 Token 为空");
                throw new Exception("获取七牛云视频上传凭证失败");
            }
            if (progress != null) progress.Report(15.0);
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

            // 2. 解析 Token 目标 Bucket
            string targetBucket = ParseBucketFromToken(uploadToken);
            if (string.IsNullOrEmpty(targetBucket)) targetBucket = VideoBucket;
            AppLogger.Log("QiniuUpload", "视频上传目标 Bucket: " + targetBucket);

            // 3. 读取视频二进制
            AppLogger.Log("QiniuUpload", "正在读取视频二进制流...");
            byte[] fileBytes;
            using (var stream = await file.OpenStreamForReadAsync())
            {
                fileBytes = new byte[stream.Length];
                int totalRead = 0;
                while (totalRead < fileBytes.Length)
                {
                    if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();
                    int chunk = await stream.ReadAsync(fileBytes, totalRead, fileBytes.Length - totalRead);
                    if (chunk <= 0) break;
                    totalRead += chunk;
                }
            }

            if (fileBytes == null || fileBytes.Length == 0)
            {
                AppLogger.Log("QiniuUpload", "错误: 读取到的视频文件字节为空");
                throw new Exception("读取视频文件为空");
            }

            string md5 = CalculateMD5(fileBytes);
            string ext = file.FileType.ToLowerInvariant().TrimStart('.');
            if (string.IsNullOrEmpty(ext)) ext = "mp4";

            string fileKey = string.Format("{0}.{1}", md5, ext);
            string mimeType = GetMimeType(ext);

            AppLogger.Log("QiniuUpload", string.Format("视频解析成功: Key={0}, Size={1} bytes, MIME={2}", fileKey, fileBytes.Length, mimeType));

            // 4. 查询七牛上传 Host
            string uploadHost = await QueryUploadHostAsync(uploadToken, targetBucket);
            AppLogger.Log("QiniuUpload", "七牛目标上传 Host: " + uploadHost);
            if (progress != null) progress.Report(20.0);
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

            // 5. 手工封装整块 Multipart 二进制 Payload
            string boundary = "----YunhuWPBoundary" + DateTime.UtcNow.Ticks.ToString("x");
            byte[] multipartPayload = BuildMultipartPayload(boundary, uploadToken, fileKey, fileBytes, mimeType);
            AppLogger.Log("QiniuUpload", string.Format("Multipart 表单构建完成，总 Payload 长度={0} 字节", multipartPayload.Length));

            // 6. 执行直传
            string respJson = await DirectUploadPayloadAsync(uploadHost, boundary, multipartPayload, progress, cancellationToken);

            long finalSize = fileBytes.Length;
            fileBytes = null;
            multipartPayload = null;
            GC.Collect();

            if (progress != null) progress.Report(100.0);

            string baseUrl = GetBaseUrlByBucket(targetBucket);
            string finalUrl = baseUrl + fileKey;

            var res = new QiniuUploadResult
            {
                Key = fileKey,
                Hash = md5,
                FileSize = finalSize,
                FileName = file.Name,
                FileExtension = ext,
                MimeType = mimeType,
                PublicUrl = finalUrl,
                Width = 0,
                Height = 0
            };

            if (!string.IsNullOrEmpty(respJson))
            {
                try
                {
                    JsonObject json;
                    if (JsonObject.TryParse(respJson, out json))
                    {
                        if (json.ContainsKey("key")) res.Key = json.GetNamedString("key");
                        if (json.ContainsKey("hash")) res.Hash = json.GetNamedString("hash");
                        if (json.ContainsKey("fsize")) res.FileSize = (long)json.GetNamedNumber("fsize");
                        if (json.ContainsKey("avinfo"))
                        {
                            var avinfo = json.GetNamedObject("avinfo");
                            if (avinfo.ContainsKey("video"))
                            {
                                var v = avinfo.GetNamedObject("video");
                                if (v.ContainsKey("width")) res.Width = (int)v.GetNamedNumber("width");
                                if (v.ContainsKey("height")) res.Height = (int)v.GetNamedNumber("height");
                            }
                        }
                    }
                }
                catch { }
            }

            AppLogger.Log("QiniuUpload", string.Format("视频直传成功: Key={0}, Hash={1}, Width={2}, Height={3}, URL={4}", res.Key, res.Hash, res.Width, res.Height, res.PublicUrl));
            return res;
        }

        /// <summary>
        /// 上传本地视频文件到七牛云并返回公网访问 URL (GET /v1/misc/qiniu-token-video)
        /// </summary>
        public static async Task<string> UploadVideoAsync(StorageFile file, string userToken, IProgress<double> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var res = await UploadVideoDetailedAsync(file, userToken, progress, cancellationToken);
            return res != null ? res.PublicUrl : null;
        }

        /// <summary>
        /// 上传本地普通文件到七牛云并返回公网访问 URL (GET /v1/misc/qiniu-token2)
        /// </summary>
        public static async Task<string> UploadFileAsync(StorageFile file, string userToken, IProgress<double> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (file == null) throw new ArgumentNullException("file");
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

            if (progress != null) progress.Report(5.0);
            AppLogger.Log("QiniuUpload", string.Format("开始处理本地文件: Name={0}, Path={1}", file.Name, file.Path));

            // 1. 获取文件专门的七牛上传 Token (/v1/misc/qiniu-token2)
            AppLogger.Log("QiniuUpload", "正在请求文件七牛上传凭证 /v1/misc/qiniu-token2 ...");
            string uploadToken = await GetQiniuTokenAsync("/v1/misc/qiniu-token2", userToken);
            if (string.IsNullOrEmpty(uploadToken))
            {
                AppLogger.Log("QiniuUpload", "错误: 获取七牛云文件上传 Token 为空");
                throw new Exception("获取七牛云文件上传凭证失败");
            }
            if (progress != null) progress.Report(15.0);
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

            // 2. 解析 Token 目标 Bucket
            string targetBucket = ParseBucketFromToken(uploadToken);
            if (string.IsNullOrEmpty(targetBucket)) targetBucket = FileBucket;
            AppLogger.Log("QiniuUpload", "文件上传目标 Bucket: " + targetBucket);

            // 3. 读取文件二进制
            AppLogger.Log("QiniuUpload", "正在读取文件二进制流...");
            byte[] fileBytes;
            using (var stream = await file.OpenStreamForReadAsync())
            {
                fileBytes = new byte[stream.Length];
                int totalRead = 0;
                while (totalRead < fileBytes.Length)
                {
                    if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();
                    int chunk = await stream.ReadAsync(fileBytes, totalRead, fileBytes.Length - totalRead);
                    if (chunk <= 0) break;
                    totalRead += chunk;
                }
            }

            if (fileBytes == null || fileBytes.Length == 0)
            {
                throw new Exception("读取文件为空");
            }

            string md5 = CalculateMD5(fileBytes);
            string ext = file.FileType.ToLowerInvariant().TrimStart('.');
            if (string.IsNullOrEmpty(ext)) ext = "bin";

            string fileKey = string.Format("{0}.{1}", md5, ext);
            string mimeType = GetMimeType(ext);

            // 4. 查询七牛上传 Host
            string uploadHost = await QueryUploadHostAsync(uploadToken, targetBucket);
            if (progress != null) progress.Report(20.0);
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

            // 5. 手工封装 Payload
            string boundary = "----YunhuWPBoundary" + DateTime.UtcNow.Ticks.ToString("x");
            byte[] multipartPayload = BuildMultipartPayload(boundary, uploadToken, fileKey, fileBytes, mimeType);

            // 6. 直传
            await DirectUploadPayloadAsync(uploadHost, boundary, multipartPayload, progress, cancellationToken);

            if (progress != null) progress.Report(100.0);

            string baseUrl = GetBaseUrlByBucket(targetBucket);
            string finalUrl = baseUrl + fileKey;
            AppLogger.Log("QiniuUpload", "文件直传成功！公网地址: " + finalUrl);
            return finalUrl;
        }

        /// <summary>
        /// 获取专门用于视频上传的七牛 Token (/v1/misc/qiniu-token-video)
        /// </summary>
        public static async Task<string> GetVideoQiniuTokenAsync(string userToken)
        {
            return await GetQiniuTokenAsync("/v1/misc/qiniu-token-video", userToken);
        }

        /// <summary>
        /// 获取专门用于普通文件上传的七牛 Token (/v1/misc/qiniu-token2)
        /// </summary>
        public static async Task<string> GetFileQiniuTokenAsync(string userToken)
        {
            return await GetQiniuTokenAsync("/v1/misc/qiniu-token2", userToken);
        }

        /// <summary>
        /// 从七牛 Upload Token 的 PutPolicy 中解析目标 Bucket 名称
        /// </summary>
        private static string ParseBucketFromToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return ImageBucket;
            try
            {
                var parts = token.Split(':');
                if (parts.Length >= 3)
                {
                    string encodedPolicy = parts[2];
                    string base64 = encodedPolicy.Replace('-', '+').Replace('_', '/');
                    int mod4 = base64.Length % 4;
                    if (mod4 > 0) base64 += new string('=', 4 - mod4);
                    byte[] policyBytes = Convert.FromBase64String(base64);
                    string jsonStr = Encoding.UTF8.GetString(policyBytes, 0, policyBytes.Length);
                    JsonObject obj;
                    if (JsonObject.TryParse(jsonStr, out obj))
                    {
                        if (obj.ContainsKey("scope"))
                        {
                            string scope = obj.GetNamedString("scope");
                            string bucket = scope.Split(':')[0];
                            if (!string.IsNullOrEmpty(bucket)) return bucket;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("QiniuUpload", "解析 PutPolicy Bucket 异常: " + ex.Message);
            }
            return ImageBucket;
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
                    else if (obj.ContainsKey("data") && obj.GetNamedValue("data").ValueType == JsonValueType.String)
                    {
                        return obj.GetNamedString("data");
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
        /// 使用多通道策略执行直传 (通道 1: HttpWebRequest; 通道 2: WinRT 单块 BufferContent; 支持进度报告与即时取消)
        /// </summary>
        private static async Task<string> DirectUploadPayloadAsync(string uploadHost, string boundary, byte[] payload, IProgress<double> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            Exception lastEx = null;
            string[] hostsToTry = new string[] { uploadHost, "upload-z2.qiniup.com", "up-z2.qiniup.com" };
            string[] protos = new string[] { "http", "https" };

            foreach (var h in hostsToTry)
            {
                foreach (var proto in protos)
                {
                    if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

                    string targetUrl = string.Format("{0}://{1}/", proto, h);
                    AppLogger.Log("QiniuUpload", "尝试通道 1 (HttpWebRequest) -> " + targetUrl);

                    try
                    {
                        var request = (HttpWebRequest)WebRequest.Create(new Uri(targetUrl));
                        request.Method = "POST";
                        request.ContentType = "multipart/form-data; boundary=" + boundary;
                        request.Headers["User-Agent"] = "QiniuDart";

                        using (var reg = cancellationToken.Register(() => { try { request.Abort(); } catch { } }))
                        {
                            using (var reqStream = await request.GetRequestStreamAsync())
                            {
                                int chunkSize = 32768; // 32 KB
                                int sent = 0;
                                while (sent < payload.Length)
                                {
                                    if (cancellationToken.IsCancellationRequested)
                                    {
                                        request.Abort();
                                        throw new OperationCanceledException();
                                    }
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
                                return respText;
                            }
                        }
                    }
                    catch (WebException wex)
                    {
                        if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();
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
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();
                        AppLogger.Log("QiniuUpload", "通道 1 异常: " + ex.Message);
                        lastEx = ex;
                    }
                }
            }

            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

            // 若通道 1 未成功且未取消，尝试通道 2
            foreach (var h in hostsToTry)
            {
                foreach (var proto in protos)
                {
                    if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();
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
                                return respBody;
                            }
                            else
                            {
                                string errBody = await resp.Content.ReadAsStringAsync();
                                AppLogger.Log("QiniuUpload", string.Format("通道 2 返回错误 ({0}): {1}", (int)resp.StatusCode, errBody));
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();
                        AppLogger.Log("QiniuUpload", "通道 2 异常: " + ex.Message);
                        lastEx = ex;
                    }
                }
            }

            if (lastEx != null)
            {
                throw lastEx;
            }

            return null;
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
                case "mp4": return "video/mp4";
                case "mov": return "video/quicktime";
                case "wmv": return "video/x-ms-wmv";
                case "avi": return "video/x-msvideo";
                case "3gp": return "video/3gpp";
                case "mkv": return "video/x-matroska";
                case "webm": return "video/webm";
                case "png": return "image/png";
                case "gif": return "image/gif";
                case "webp": return "image/webp";
                case "bmp": return "image/bmp";
                case "jpeg":
                case "jpg":
                    return "image/jpeg";
                default:
                    return "application/octet-stream";
            }
        }
    }
}
