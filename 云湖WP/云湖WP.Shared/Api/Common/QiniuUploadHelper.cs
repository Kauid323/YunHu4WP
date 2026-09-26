using System;
using System.Collections.Generic;
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
using Windows.Storage.Streams;
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
    /// 七牛云客户端流式直传助手 (支持图片、视频、音频、文件专用 Token 直传、低内存消耗、智能区域域名重定向、实时进度与取消操作)
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

        /// <summary>
        /// 存储 Bucket 对应的上传接入点缓存（支持接收七牛 incorrect region 推荐域名后自动更新）
        /// </summary>
        private static readonly Dictionary<string, string> BucketHostCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ImageBucket, "upload-z2.qiniup.com" },
            { VideoBucket, "upload-z2.qiniup.com" },
            { AudioBucket, "upload-z2.qiniup.com" },
            { FileBucket, "upload-cn-east-2.qiniup.com" }
        };

        /// <summary>
        /// 更新指定 Bucket 对应的接入点域名缓存（下次及重试直接使用新域名）
        /// </summary>
        public static void UpdateBucketHost(string bucket, string host)
        {
            if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(host)) return;
            lock (BucketHostCache)
            {
                BucketHostCache[bucket] = host;
            }
            AppLogger.Log("QiniuUpload", string.Format("已更新 Bucket [{0}] 接入点域名缓存为: {1}", bucket, host));
        }

        /// <summary>
        /// 获取缓存中指定 Bucket 对应的接入点域名
        /// </summary>
        public static string GetCachedBucketHost(string bucket)
        {
            if (string.IsNullOrEmpty(bucket)) return "upload-z2.qiniup.com";
            lock (BucketHostCache)
            {
                string host;
                if (BucketHostCache.TryGetValue(bucket, out host) && !string.IsNullOrEmpty(host))
                {
                    return host;
                }
            }
            return (bucket.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0 || bucket.IndexOf("disk", StringComparison.OrdinalIgnoreCase) >= 0)
                ? "upload-cn-east-2.qiniup.com"
                : "upload-z2.qiniup.com";
        }

        private static string GetBaseUrlByBucket(string bucket)
        {
            if (string.IsNullOrEmpty(bucket)) return ImageBaseUrl;
            if (bucket.IndexOf("video", StringComparison.OrdinalIgnoreCase) >= 0) return VideoBaseUrl;
            if (bucket.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0) return AudioBaseUrl;
            if (bucket.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0 || bucket.IndexOf("disk", StringComparison.OrdinalIgnoreCase) >= 0) return FileBaseUrl;
            return ImageBaseUrl;
        }

        /// <summary>
        /// 从七牛错误信息中解析 incorrect region, please use <host> 推荐域名
        /// </summary>
        public static string ParseRedirectHostFromError(string errText)
        {
            if (string.IsNullOrEmpty(errText)) return null;
            int idx = errText.IndexOf("please use ", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int start = idx + "please use ".Length;
                int end = errText.IndexOfAny(new char[] { ',', ' ', '"', '\'', '}', '\r', '\n' }, start);
                if (end > start)
                {
                    return errText.Substring(start, end - start).Trim();
                }
                else if (start < errText.Length)
                {
                    return errText.Substring(start).Trim();
                }
            }
            return null;
        }

        /// <summary>
        /// 计算文件的 MD5 哈希（基于 64KB 分块流式计算，避免一次性加载大文件到内存引起 OOM）
        /// </summary>
        public static async Task<string> CalculateFileMD5Async(StorageFile file, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (file == null) return "";
            var alg = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Md5);
            var hasher = alg.CreateHash();

            using (var stream = await file.OpenStreamForReadAsync())
            {
                byte[] buffer = new byte[65536]; // 64 KB 缓冲区
                while (true)
                {
                    if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (read <= 0) break;

                    if (read == buffer.Length)
                    {
                        var ibuf = CryptographicBuffer.CreateFromByteArray(buffer);
                        hasher.Append(ibuf);
                    }
                    else
                    {
                        byte[] lastChunk = new byte[read];
                        System.Buffer.BlockCopy(buffer, 0, lastChunk, 0, read);
                        var ibuf = CryptographicBuffer.CreateFromByteArray(lastChunk);
                        hasher.Append(ibuf);
                    }
                }
            }

            var hashed = hasher.GetValueAndReset();
            return CryptographicBuffer.EncodeToHexString(hashed).ToLowerInvariant();
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

            // 2. 流式计算 MD5 与获取属性
            string md5 = await CalculateFileMD5Async(file, cancellationToken);
            string ext = file.FileType.ToLowerInvariant().TrimStart('.');
            if (string.IsNullOrEmpty(ext)) ext = "jpg";

            string fileKey = string.Format("{0}.{1}", md5, ext);
            string mimeType = GetMimeType(ext);

            var props = await file.GetBasicPropertiesAsync();
            long fileSize = (long)props.Size;

            AppLogger.Log("QiniuUpload", string.Format("图片解析成功: Key={0}, Size={1} bytes, MIME={2}", fileKey, fileSize, mimeType));

            // 3. 查询七牛上传 Host
            string uploadHost = await QueryUploadHostAsync(uploadToken, targetBucket);
            AppLogger.Log("QiniuUpload", "七牛目标上传 Host: " + uploadHost);
            if (progress != null) progress.Report(20.0);
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

            // 4. 流式执行直传（零大内存分配）
            string boundary = "----YunhuWPBoundary" + DateTime.UtcNow.Ticks.ToString("x");
            await DirectUploadFileStreamAsync(file, uploadHost, boundary, uploadToken, fileKey, mimeType, targetBucket, file.Name, progress, cancellationToken);

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

            // 3. 流式计算 MD5
            string md5 = await CalculateFileMD5Async(file, cancellationToken);
            string ext = file.FileType.ToLowerInvariant().TrimStart('.');
            if (string.IsNullOrEmpty(ext)) ext = "mp4";

            string fileKey = string.Format("{0}.{1}", md5, ext);
            string mimeType = GetMimeType(ext);

            var props = await file.GetBasicPropertiesAsync();
            long fileSize = (long)props.Size;

            AppLogger.Log("QiniuUpload", string.Format("视频解析成功: Key={0}, Size={1} bytes, MIME={2}", fileKey, fileSize, mimeType));

            // 4. 查询七牛上传 Host
            string uploadHost = await QueryUploadHostAsync(uploadToken, targetBucket);
            AppLogger.Log("QiniuUpload", "七牛目标上传 Host: " + uploadHost);
            if (progress != null) progress.Report(20.0);
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

            // 5. 流式执行直传（零大内存分配）
            string boundary = "----YunhuWPBoundary" + DateTime.UtcNow.Ticks.ToString("x");
            string respJson = await DirectUploadFileStreamAsync(file, uploadHost, boundary, uploadToken, fileKey, mimeType, targetBucket, file.Name, progress, cancellationToken);

            if (progress != null) progress.Report(100.0);

            string baseUrl = GetBaseUrlByBucket(targetBucket);
            string finalUrl = baseUrl + fileKey;

            var res = new QiniuUploadResult
            {
                Key = fileKey,
                Hash = md5,
                FileSize = fileSize,
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
        /// 上传本地普通文件到七牛云并返回详细结构化信息 (GET /v1/misc/qiniu-token2)
        /// </summary>
        public static async Task<QiniuUploadResult> UploadFileDetailedAsync(StorageFile file, string userToken, IProgress<double> progress = null, CancellationToken cancellationToken = default(CancellationToken))
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

            // 2. 解析 Token 目标 Bucket (默认 chat68-file)
            string targetBucket = ParseBucketFromToken(uploadToken);
            if (string.IsNullOrEmpty(targetBucket)) targetBucket = FileBucket;
            AppLogger.Log("QiniuUpload", "文件上传目标 Bucket: " + targetBucket);

            // 3. 流式计算 MD5
            string md5 = await CalculateFileMD5Async(file, cancellationToken);
            string ext = file.FileType.ToLowerInvariant().TrimStart('.');
            if (string.IsNullOrEmpty(ext)) ext = "dat";

            // 文件 Key 规范：disk/MD5.扩展名
            string fileKey = string.Format("disk/{0}.{1}", md5, ext);
            string mimeType = GetMimeType(ext);

            var props = await file.GetBasicPropertiesAsync();
            long fileSize = (long)props.Size;

            AppLogger.Log("QiniuUpload", string.Format("文件解析成功: Key={0}, Size={1} bytes, MIME={2}", fileKey, fileSize, mimeType));

            // 4. 查询七牛上传 Host
            string uploadHost = await QueryUploadHostAsync(uploadToken, targetBucket);
            AppLogger.Log("QiniuUpload", "七牛目标上传 Host: " + uploadHost);
            if (progress != null) progress.Report(20.0);
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

            // 5. 流式执行直传（零大内存分配）
            string boundary = "----YunhuWPBoundary" + DateTime.UtcNow.Ticks.ToString("x");
            string respJson = await DirectUploadFileStreamAsync(file, uploadHost, boundary, uploadToken, fileKey, mimeType, targetBucket, file.Name, progress, cancellationToken);

            if (progress != null) progress.Report(100.0);

            string baseUrl = GetBaseUrlByBucket(targetBucket);
            string finalUrl = baseUrl + fileKey;

            var res = new QiniuUploadResult
            {
                Key = fileKey,
                Hash = md5,
                FileSize = fileSize,
                FileName = file.Name,
                FileExtension = ext,
                MimeType = mimeType,
                PublicUrl = finalUrl
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
                    }
                }
                catch { }
            }

            AppLogger.Log("QiniuUpload", string.Format("文件直传成功: Key={0}, Hash={1}, Size={2}, URL={3}", res.Key, res.Hash, res.FileSize, res.PublicUrl));
            return res;
        }

        /// <summary>
        /// 上传本地普通文件到七牛云并返回公网访问 URL (GET /v1/misc/qiniu-token2)
        /// </summary>
        public static async Task<string> UploadFileAsync(StorageFile file, string userToken, IProgress<double> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var res = await UploadFileDetailedAsync(file, userToken, progress, cancellationToken);
            return res != null ? res.PublicUrl : null;
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
            string defaultHost = GetCachedBucketHost(bucket);
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
                                        UpdateBucketHost(bucket, queried);
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
                AppLogger.Log("QiniuUpload", "查询七牛 Host 异常 (将使用缓存/默认节点): " + ex.Message);
            }

            return defaultHost;
        }

        /// <summary>
        /// 使用流式多通道策略执行七牛云直传（避免将文件整个加载到内存中导致 OutOfMemoryException）
        /// 通道 1: WinRT HttpMultipartFormDataContent + HttpStreamContent (原生流式直传)
        /// 通道 2: HttpWebRequest + 64KB 分块流式写入 (分块流式回退)
        /// </summary>
        private static async Task<string> DirectUploadFileStreamAsync(
            StorageFile file,
            string uploadHost,
            string boundary,
            string uploadToken,
            string fileKey,
            string mimeType,
            string targetBucket,
            string fileName = null,
            IProgress<double> progress = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Exception lastEx = null;
            string actualFileName = !string.IsNullOrEmpty(fileName) ? fileName : (!string.IsNullOrEmpty(file.Name) ? file.Name : fileKey);
            
            // 构造候选 Host 列表（按优先级排列并去重）
            var hostsToTry = new List<string>();
            string cachedHost = GetCachedBucketHost(targetBucket);
            
            Action<string> addHost = h =>
            {
                if (!string.IsNullOrEmpty(h) && !hostsToTry.Contains(h))
                {
                    hostsToTry.Add(h);
                }
            };

            addHost(uploadHost);
            addHost(cachedHost);
            if (targetBucket.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0 || targetBucket.IndexOf("disk", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                addHost("upload-cn-east-2.qiniup.com");
                addHost("up-cn-east-2.qiniup.com");
            }
            addHost("upload-z2.qiniup.com");
            addHost("up-z2.qiniup.com");

            string[] protos = new string[] { "https", "http" };

            // 通道 1: WinRT HttpClient + HttpStreamContent (底层无托管内存拷贝)
            for (int i = 0; i < hostsToTry.Count; i++)
            {
                string h = hostsToTry[i];
                if (string.IsNullOrEmpty(h)) continue;

                foreach (var proto in protos)
                {
                    if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();
                    string targetUrl = string.Format("{0}://{1}/", proto, h);
                    AppLogger.Log("QiniuUpload", "尝试流式通道 1 (WinRT HttpMultipartFormDataContent) -> " + targetUrl);

                    string respBody = null;
                    bool isSuccess = false;
                    string errBody = null;
                    int statusCode = 0;

                    try
                    {
                        var filter = new HttpBaseProtocolFilter();
                        filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Untrusted);
                        filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.InvalidName);
                        filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Expired);
                        filter.AllowAutoRedirect = true;

                        using (var client = new HttpClient(filter))
                        using (var form = new HttpMultipartFormDataContent(boundary))
                        {
                            client.DefaultRequestHeaders.TryAppendWithoutValidation("User-Agent", "QiniuDart");

                            // 七牛要求 token 和 key 字段置于 file 前面
                            form.Add(new HttpStringContent(uploadToken), "token");
                            form.Add(new HttpStringContent(fileKey), "key");

                            using (var fileStream = await file.OpenStreamForReadAsync())
                            {
                                var inputStream = fileStream.AsInputStream();
                                var streamContent = new HttpStreamContent(inputStream);
                                if (!string.IsNullOrEmpty(mimeType))
                                {
                                    try
                                    {
                                        streamContent.Headers.ContentType = new HttpMediaTypeHeaderValue(mimeType);
                                    }
                                    catch { }
                                }
                                form.Add(streamContent, "file", actualFileName);

                                var progressHandler = new Progress<HttpProgress>(p =>
                                {
                                    if (progress != null && p.TotalBytesToSend.HasValue && p.TotalBytesToSend.Value > 0)
                                    {
                                        double pct = 20.0 + ((double)p.BytesSent / (double)p.TotalBytesToSend.Value) * 75.0;
                                        if (pct > 98.0) pct = 98.0;
                                        progress.Report(pct);
                                    }
                                });

                                var resp = await client.PostAsync(new Uri(targetUrl), form).AsTask(cancellationToken, progressHandler);
                                statusCode = (int)resp.StatusCode;
                                if (resp.IsSuccessStatusCode)
                                {
                                    respBody = await resp.Content.ReadAsStringAsync();
                                    isSuccess = true;
                                }
                                else
                                {
                                    errBody = await resp.Content.ReadAsStringAsync();
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (!isSuccess)
                        {
                            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();
                            AppLogger.Log("QiniuUpload", "通道 1 异常: " + ex.Message);
                            lastEx = ex;
                        }
                    }

                    if (isSuccess && !string.IsNullOrEmpty(respBody))
                    {
                        AppLogger.Log("QiniuUpload", "通道 1 流式上传成功: " + respBody);
                        UpdateBucketHost(targetBucket, h);
                        return respBody;
                    }
                    else if (!string.IsNullOrEmpty(errBody))
                    {
                        AppLogger.Log("QiniuUpload", string.Format("通道 1 返回错误 ({0}): {1}", statusCode, errBody));
                        
                        // 检查是否包含七牛区域重定向指示
                        string redirected = ParseRedirectHostFromError(errBody);
                        if (!string.IsNullOrEmpty(redirected))
                        {
                            AppLogger.Log("QiniuUpload", "检测到七牛区域重定向接入点: " + redirected + "，已更新缓存并在下次/重试中优先使用！");
                            UpdateBucketHost(targetBucket, redirected);
                            if (!hostsToTry.Contains(redirected))
                            {
                                hostsToTry.Insert(i + 1, redirected);
                            }
                        }

                        lastEx = new Exception(string.Format("七牛上传返回 (HTTP {0}): {1}", statusCode, errBody));
                    }
                }
            }

            // 通道 2: HttpWebRequest 64KB 分块流式写入回退
            for (int i = 0; i < hostsToTry.Count; i++)
            {
                string h = hostsToTry[i];
                if (string.IsNullOrEmpty(h)) continue;

                foreach (var proto in protos)
                {
                    if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();
                    string targetUrl = string.Format("{0}://{1}/", proto, h);
                    AppLogger.Log("QiniuUpload", "尝试流式通道 2 (HttpWebRequest 64KB Streaming) -> " + targetUrl);

                    string respText = null;
                    bool isSuccess = false;

                    try
                    {
                        var request = (HttpWebRequest)WebRequest.Create(new Uri(targetUrl));
                        request.Method = "POST";
                        request.ContentType = "multipart/form-data; boundary=" + boundary;
                        request.Headers["User-Agent"] = "QiniuDart";

                        byte[] tokenHeader = Encoding.UTF8.GetBytes(
                            string.Format("--{0}\r\nContent-Disposition: form-data; name=\"token\"\r\n\r\n{1}\r\n", boundary, uploadToken));
                        byte[] keyHeader = Encoding.UTF8.GetBytes(
                            string.Format("--{0}\r\nContent-Disposition: form-data; name=\"key\"\r\n\r\n{1}\r\n", boundary, fileKey));
                        byte[] fileHeader = Encoding.UTF8.GetBytes(
                            string.Format("--{0}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{1}\"\r\nContent-Type: {2}\r\n\r\n", boundary, actualFileName, mimeType));
                        byte[] footer = Encoding.UTF8.GetBytes(
                            string.Format("\r\n--{0}--\r\n", boundary));

                        var props = await file.GetBasicPropertiesAsync();
                        long fileSize = (long)props.Size;

                        using (var reg = cancellationToken.Register(() => { try { request.Abort(); } catch { } }))
                        {
                            using (var reqStream = await request.GetRequestStreamAsync())
                            {
                                await reqStream.WriteAsync(tokenHeader, 0, tokenHeader.Length);
                                await reqStream.WriteAsync(keyHeader, 0, keyHeader.Length);
                                await reqStream.WriteAsync(fileHeader, 0, fileHeader.Length);

                                using (var fileStream = await file.OpenStreamForReadAsync())
                                {
                                    byte[] chunkBuf = new byte[65536];
                                    long sentBytes = 0;
                                    while (true)
                                    {
                                        if (cancellationToken.IsCancellationRequested)
                                        {
                                            request.Abort();
                                            throw new OperationCanceledException();
                                        }
                                        int read = await fileStream.ReadAsync(chunkBuf, 0, chunkBuf.Length);
                                        if (read <= 0) break;
                                        await reqStream.WriteAsync(chunkBuf, 0, read);
                                        sentBytes += read;

                                        if (progress != null && fileSize > 0)
                                        {
                                            double pct = 20.0 + ((double)sentBytes / (double)fileSize) * 75.0;
                                            if (pct > 98.0) pct = 98.0;
                                            progress.Report(pct);
                                        }
                                    }
                                }

                                await reqStream.WriteAsync(footer, 0, footer.Length);
                                await reqStream.FlushAsync();
                            }

                            using (var resp = (HttpWebResponse)await request.GetResponseAsync())
                            using (var respStream = resp.GetResponseStream())
                            using (var reader = new StreamReader(respStream))
                            {
                                respText = await reader.ReadToEndAsync();
                                isSuccess = true;
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
                        AppLogger.Log("QiniuUpload", "通道 2 失败: " + errDetail);

                        string redirected = ParseRedirectHostFromError(errDetail);
                        if (!string.IsNullOrEmpty(redirected))
                        {
                            AppLogger.Log("QiniuUpload", "通道 2 检测到七牛区域重定向接入点: " + redirected + "，已更新缓存！");
                            UpdateBucketHost(targetBucket, redirected);
                            if (!hostsToTry.Contains(redirected))
                            {
                                hostsToTry.Insert(i + 1, redirected);
                            }
                        }

                        lastEx = new Exception("七牛上传返回: " + errDetail, wex);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (!isSuccess)
                        {
                            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();
                            AppLogger.Log("QiniuUpload", "通道 2 异常: " + ex.Message);
                            lastEx = ex;
                        }
                    }

                    if (isSuccess && !string.IsNullOrEmpty(respText))
                    {
                        AppLogger.Log("QiniuUpload", "通道 2 流式上传成功: " + respText);
                        UpdateBucketHost(targetBucket, h);
                        return respText;
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
                case "mp3": return "audio/mp3";
                case "wav": return "audio/wav";
                case "aac": return "audio/aac";
                case "m4a": return "audio/mp4";
                case "flac": return "audio/flac";
                case "png": return "image/png";
                case "gif": return "image/gif";
                case "webp": return "image/webp";
                case "bmp": return "image/bmp";
                case "jpeg":
                case "jpg":
                    return "image/jpeg";
                case "pdf": return "application/pdf";
                case "zip": return "application/zip";
                case "rar": return "application/x-rar-compressed";
                case "7z": return "application/x-7z-compressed";
                case "txt": return "text/plain";
                case "doc": return "application/msword";
                case "docx": return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                case "xls": return "application/vnd.ms-excel";
                case "xlsx": return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                case "ppt": return "application/vnd.ms-powerpoint";
                case "pptx": return "application/vnd.openxmlformats-officedocument.presentationml.presentation";
                default:
                    return "application/octet-stream";
            }
        }
    }
}
