using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Certificates;
using Windows.Security.Cryptography.Core;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Web.Http;
using Windows.Web.Http.Filters;

namespace 云湖WP.Utils
{
    /// <summary>
    /// 高性能头像与图片加载器 (支持每个头像独一无二的随机本地文件名持久化缓存、Referer、96x96 硬件低内存解码)
    /// </summary>
    public static class ImageLoader
    {
        public const string SettingKeyAvatarThreads = "AvatarLoadThreads";
        public const string SettingKeyDisableAllImages = "DisableAllImages";
        private const string SettingKeyFileMapPrefix = "AvatarMap_";
        public const int DefaultAvatarThreads = 4;
        public const int MinThreads = 1;
        public const int MaxThreads = 16;

        private const string CacheFolderName = "AvatarCache";
        private const string RefererUrlHttps = "https://myapp.jwznb.com";
        private const string RefererUrlHttp = "http://myapp.jwznb.com";
        private const string UserAgent = "Mozilla/5.0 (Windows Phone 8.1; ARM; Trident/7.0; Touch; rv:11.0; IEMobile/11.0; NOKIA; Lumia 930) like Gecko";

        private static int _maxConcurrentLoads = DefaultAvatarThreads;
        private static bool _disableAllImages = false;
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(DefaultAvatarThreads, DefaultAvatarThreads);
        private static readonly object _syncLock = new object();

        // 内存字节缓存与 BitmapImage 缓存
        private static readonly Dictionary<string, byte[]> _memoryCache = new Dictionary<string, byte[]>();
        private static readonly Dictionary<string, BitmapImage> _bitmapCache = new Dictionary<string, BitmapImage>();

        // 每个头像 URL 映射的独一无二的本地随机文件名映射
        private static readonly Dictionary<string, string> _avatarFileMap = new Dictionary<string, string>();

        // 正在进行的下载任务缓存，避免相同 URL 并发重复下载
        private static readonly Dictionary<string, Task<byte[]>> _inFlightTasks = new Dictionary<string, Task<byte[]>>();

        // 磁盘缓存文件索引 (避免调用 GetFileAsync 产生 FileNotFoundException 异常)
        private static readonly HashSet<string> _knownCacheFiles = new HashSet<string>();
        private static bool _cacheFolderScanned = false;
        private static StorageFolder _cacheFolder;

        private static HttpClient _httpClient;

        static ImageLoader()
        {
            InitSettings();
            InitHttpClient();
        }

        private static void InitSettings()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values.ContainsKey(SettingKeyAvatarThreads))
                {
                    int threads = Convert.ToInt32(localSettings.Values[SettingKeyAvatarThreads]);
                    _maxConcurrentLoads = Math.Max(MinThreads, Math.Min(MaxThreads, threads));
                }
                else
                {
                    _maxConcurrentLoads = DefaultAvatarThreads;
                }

                if (localSettings.Values.ContainsKey(SettingKeyDisableAllImages))
                {
                    _disableAllImages = Convert.ToBoolean(localSettings.Values[SettingKeyDisableAllImages]);
                }
                else
                {
                    _disableAllImages = false;
                }
            }
            catch
            {
                _maxConcurrentLoads = DefaultAvatarThreads;
                _disableAllImages = false;
            }

            _semaphore = new SemaphoreSlim(_maxConcurrentLoads, _maxConcurrentLoads);
        }

        private static void InitHttpClient()
        {
            try
            {
                var filter = new HttpBaseProtocolFilter();
                // 忽略过期的 Windows Phone 8.1 根证书导致的安全连接校验失败
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Untrusted);
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.InvalidName);
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Expired);
                filter.AllowAutoRedirect = true;

                _httpClient = new HttpClient(filter);
                _httpClient.DefaultRequestHeaders.TryAppendWithoutValidation("Referer", RefererUrlHttps);
                _httpClient.DefaultRequestHeaders.TryAppendWithoutValidation("User-Agent", UserAgent);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("InitHttpClient failed: " + ex.Message);
                try
                {
                    _httpClient = new HttpClient();
                    _httpClient.DefaultRequestHeaders.TryAppendWithoutValidation("Referer", RefererUrlHttps);
                    _httpClient.DefaultRequestHeaders.TryAppendWithoutValidation("User-Agent", UserAgent);
                }
                catch { }
            }
        }

        /// <summary>
        /// 获取或设置是否完全禁止加载任何网络/本地图片与头像 (极速省流纯净模式)
        /// </summary>
        public static bool DisableAllImages
        {
            get { return _disableAllImages; }
            set
            {
                if (_disableAllImages != value)
                {
                    _disableAllImages = value;
                    try
                    {
                        var localSettings = ApplicationData.Current.LocalSettings;
                        localSettings.Values[SettingKeyDisableAllImages] = _disableAllImages;
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// 获取或设置每秒并发图片加载线程数
        /// </summary>
        public static int MaxConcurrentLoads
        {
            get { return _maxConcurrentLoads; }
            set
            {
                int val = Math.Max(MinThreads, Math.Min(MaxThreads, value));
                if (_maxConcurrentLoads != val)
                {
                    _maxConcurrentLoads = val;
                    try
                    {
                        var localSettings = ApplicationData.Current.LocalSettings;
                        localSettings.Values[SettingKeyAvatarThreads] = _maxConcurrentLoads;
                    }
                    catch { }

                    lock (_syncLock)
                    {
                        _semaphore = new SemaphoreSlim(_maxConcurrentLoads, _maxConcurrentLoads);
                    }
                }
            }
        }

        /// <summary>
        /// 异步加载头像为 BitmapImage (包含七牛云 96x96 处理、Referer 请求头、本地独一无二随机文件缓存及并发控制)
        /// </summary>
        public static async Task<BitmapImage> LoadAvatarAsync(string rawUrl, int width = 96, int height = 96)
        {
            if (_disableAllImages) return null;
            if (string.IsNullOrEmpty(rawUrl)) return null;

            string finalUrl = ImageHelper.FormatQiniuUrl(rawUrl, width, height);
            if (string.IsNullOrEmpty(finalUrl)) return null;

            // 检查已解码的 BitmapImage 缓存
            lock (_syncLock)
            {
                if (_bitmapCache.ContainsKey(finalUrl))
                {
                    return _bitmapCache[finalUrl];
                }
            }

            byte[] bytes = await GetImageBytesAsync(finalUrl);
            if (bytes == null || bytes.Length == 0) return null;

            var bmp = await BytesToBitmapImageAsync(bytes, width, height);
            if (bmp != null)
            {
                lock (_syncLock)
                {
                    _bitmapCache[finalUrl] = bmp;
                }
            }
            return bmp;
        }

        /// <summary>
        /// 获取图片字节数据 (优先读取内存，其次独立随机文件磁盘缓存，最后网络并发请求，全流程后台线程池执行绝不阻塞 UI 主线程)
        /// </summary>
        /// <param name="finalUrl">图片链接</param>
        /// <param name="force">是否强制下载（忽略省流无图模式，例如大图预览器点击查看）</param>
        public static Task<byte[]> GetImageBytesAsync(string finalUrl, bool force = false)
        {
            return Task.Run(async () =>
            {
                if (_disableAllImages && !force) return null;
                if (string.IsNullOrEmpty(finalUrl)) return null;

                // 1. 检查内存缓存
                lock (_syncLock)
                {
                    if (_memoryCache.ContainsKey(finalUrl))
                    {
                        return _memoryCache[finalUrl];
                    }
                }

                // 2. 检查是否有相同的请求正在飞行中
                Task<byte[]> runningTask = null;
                lock (_syncLock)
                {
                    if (_inFlightTasks.ContainsKey(finalUrl))
                    {
                        runningTask = _inFlightTasks[finalUrl];
                    }
                    else
                    {
                        runningTask = InternalFetchImageAsync(finalUrl);
                        _inFlightTasks[finalUrl] = runningTask;
                    }
                }

                try
                {
                    byte[] data = await runningTask.ConfigureAwait(false);
                    return data;
                }
                finally
                {
                    lock (_syncLock)
                    {
                        if (_inFlightTasks.ContainsKey(finalUrl))
                        {
                            _inFlightTasks.Remove(finalUrl);
                        }
                    }
                }
            });
        }

        private static async Task<byte[]> InternalFetchImageAsync(string finalUrl)
        {
            string fileName = GetOrAssignRandomFileName(finalUrl);

            // 1. 检查本地磁盘随机文件缓存
            byte[] diskData = await ReadFromDiskCacheAsync(fileName);
            if (diskData != null && diskData.Length > 0)
            {
                lock (_syncLock)
                {
                    _memoryCache[finalUrl] = diskData;
                }
                return diskData;
            }

            // 2. 通过信号量控制最大并发线程数
            await _semaphore.WaitAsync();
            try
            {
                if (_httpClient == null)
                {
                    InitHttpClient();
                }

                Uri uri;
                if (!Uri.TryCreate(finalUrl, UriKind.Absolute, out uri))
                {
                    return null;
                }

                byte[] downloadedBytes = await DownloadWithFallbackAsync(uri);
                if (downloadedBytes != null && downloadedBytes.Length > 0)
                {
                    // 存入内存
                    lock (_syncLock)
                    {
                        _memoryCache[finalUrl] = downloadedBytes;
                    }

                    // 存入本地独一无二随机文件
                    await WriteToDiskCacheAsync(fileName, downloadedBytes);

                    return downloadedBytes;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ImageLoader.InternalFetchImageAsync failed for " + finalUrl + ": " + ex.Message);
            }
            finally
            {
                _semaphore.Release();
            }

            return null;
        }

        private static async Task<byte[]> DownloadWithFallbackAsync(Uri uri)
        {
            // 首次尝试：默认 Referer (https://myapp.jwznb.com)
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, uri))
                {
                    req.Headers.TryAppendWithoutValidation("Referer", RefererUrlHttps);
                    req.Headers.TryAppendWithoutValidation("User-Agent", UserAgent);

                    using (var response = await _httpClient.SendRequestAsync(req))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            var buffer = await response.Content.ReadAsBufferAsync();
                            return buffer.ToArray();
                        }
                    }
                }
            }
            catch { }

            // 降级尝试：http Referer (http://myapp.jwznb.com)
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, uri))
                {
                    req.Headers.TryAppendWithoutValidation("Referer", RefererUrlHttp);
                    req.Headers.TryAppendWithoutValidation("User-Agent", UserAgent);

                    using (var response = await _httpClient.SendRequestAsync(req))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            var buffer = await response.Content.ReadAsBufferAsync();
                            return buffer.ToArray();
                        }
                    }
                }
            }
            catch { }

            // 降级尝试：直接使用原生 GET
            try
            {
                using (var response = await _httpClient.GetAsync(uri))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var buffer = await response.Content.ReadAsBufferAsync();
                        return buffer.ToArray();
                    }
                }
            }
            catch { }

            // 降级尝试：若为 HTTPS，降级尝试 HTTP (避开 WP8.1 过期证书/TLS 握手故障)
            if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var httpUri = new Uri("http://" + uri.Authority + uri.PathAndQuery);
                    using (var req = new HttpRequestMessage(HttpMethod.Get, httpUri))
                    {
                        req.Headers.TryAppendWithoutValidation("Referer", RefererUrlHttp);
                        req.Headers.TryAppendWithoutValidation("User-Agent", UserAgent);
                        using (var response = await _httpClient.SendRequestAsync(req))
                        {
                            if (response.IsSuccessStatusCode)
                            {
                                var buffer = await response.Content.ReadAsBufferAsync();
                                return buffer.ToArray();
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    var httpUri = new Uri("http://" + uri.Authority + uri.PathAndQuery);
                    using (var response = await _httpClient.GetAsync(httpUri))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            var buffer = await response.Content.ReadAsBufferAsync();
                            return buffer.ToArray();
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// 将图片字节流转化为 UI 绑定的 BitmapImage (带 DecodePixel 尺寸限制，大幅降低 WP8.1 内存与解码耗时)
        /// </summary>
        public static async Task<BitmapImage> BytesToBitmapImageAsync(byte[] bytes, int decodeWidth = 96, int decodeHeight = 96)
        {
            if (bytes == null || bytes.Length < 4) return null;

            // 过滤文本/HTML/JSON 错误响应或非图片数据
            if (bytes[0] == '<' || bytes[0] == '{' || bytes[0] == '[') return null;

            // 检查 Windows Phone 8.1 WIC 原生解码器支持的图片格式魔数:
            // JPEG (FF D8), PNG (89 50 4E 47), GIF (47 49 46), BMP (42 4D)
            bool isSupported = (bytes[0] == 0xFF && bytes[1] == 0xD8) ||
                               (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) ||
                               (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) ||
                               (bytes[0] == 0x42 && bytes[1] == 0x4D);
            if (!isSupported)
            {
                // 非 WP8.1 硬件解码支持的格式 (如 WebP/SVG)，安全忽略避免 0x88982F50 崩溃
                return null;
            }

            try
            {
                var bitmap = new BitmapImage();
                if (decodeWidth > 0)
                {
                    bitmap.DecodePixelWidth = decodeWidth;
                }
                if (decodeHeight > 0)
                {
                    bitmap.DecodePixelHeight = decodeHeight;
                }
                bitmap.DecodePixelType = DecodePixelType.Logical;

                using (var stream = new InMemoryRandomAccessStream())
                {
                    using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                    {
                        writer.WriteBytes(bytes);
                        await writer.StoreAsync();
                    }
                    stream.Seek(0);
                    await bitmap.SetSourceAsync(stream);
                }
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("BytesToBitmapImageAsync failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 获取或生成头像独一无二的随机本地文件名 (格式: avatar_xxxxxxxx.dat)
        /// </summary>
        private static string GetOrAssignRandomFileName(string url)
        {
            lock (_syncLock)
            {
                if (_avatarFileMap.ContainsKey(url))
                {
                    return _avatarFileMap[url];
                }

                // 尝试从 LocalSettings 索引读取
                try
                {
                    string settingKey = SettingKeyFileMapPrefix + GetSafeHashKey(url);
                    var localSettings = ApplicationData.Current.LocalSettings;
                    if (localSettings.Values.ContainsKey(settingKey))
                    {
                        string savedName = localSettings.Values[settingKey] as string;
                        if (!string.IsNullOrEmpty(savedName))
                        {
                            _avatarFileMap[url] = savedName;
                            return savedName;
                        }
                    }

                    // 生成独一无二的随机文件名
                    string randomFileName = "avatar_" + Guid.NewGuid().ToString("N") + ".dat";
                    _avatarFileMap[url] = randomFileName;
                    localSettings.Values[settingKey] = randomFileName;
                    return randomFileName;
                }
                catch
                {
                    string fallbackName = "avatar_" + Guid.NewGuid().ToString("N") + ".dat";
                    _avatarFileMap[url] = fallbackName;
                    return fallbackName;
                }
            }
        }

        private static string GetSafeHashKey(string url)
        {
            try
            {
                var alg = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Md5);
                var buff = CryptographicBuffer.ConvertStringToBinary(url, BinaryStringEncoding.Utf8);
                var hash = alg.HashData(buff);
                return CryptographicBuffer.EncodeToHexString(hash);
            }
            catch
            {
                return Math.Abs(url.GetHashCode()).ToString();
            }
        }

        private static async Task<StorageFolder> GetCacheFolderAsync()
        {
            if (_cacheFolder != null) return _cacheFolder;
            try
            {
                var localFolder = ApplicationData.Current.LocalFolder;
                _cacheFolder = await localFolder.CreateFolderAsync(CacheFolderName, CreationCollisionOption.OpenIfExists);
                return _cacheFolder;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<byte[]> ReadFromDiskCacheAsync(string fileName)
        {
            try
            {
                var folder = await GetCacheFolderAsync();
                if (folder == null) return null;

                // 首次拉取目录已知文件列表，避免逐个调用 GetFileAsync 产生 FileNotFoundException
                if (!_cacheFolderScanned)
                {
                    var files = await folder.GetFilesAsync();
                    lock (_syncLock)
                    {
                        if (!_cacheFolderScanned)
                        {
                            if (files != null)
                            {
                                foreach (var f in files)
                                {
                                    if (f != null) _knownCacheFiles.Add(f.Name);
                                }
                            }
                            _cacheFolderScanned = true;
                        }
                    }
                }

                lock (_syncLock)
                {
                    if (!_knownCacheFiles.Contains(fileName))
                    {
                        return null;
                    }
                }

                var file = await folder.GetFileAsync(fileName);
                if (file != null)
                {
                    var buffer = await FileIO.ReadBufferAsync(file);
                    return buffer.ToArray();
                }
            }
            catch { }
            return null;
        }

        private static async Task WriteToDiskCacheAsync(string fileName, byte[] bytes)
        {
            try
            {
                var folder = await GetCacheFolderAsync();
                if (folder == null || bytes == null || bytes.Length == 0) return;

                var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteBytesAsync(file, bytes);

                lock (_syncLock)
                {
                    _knownCacheFiles.Add(fileName);
                }
            }
            catch { }
        }

        /// <summary>
        /// 从内存和磁盘中移除指定 URL 的缓存项 (用于重试加载时强制刷新)
        /// </summary>
        public static async Task RemoveFromCacheAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            string fileName = null;
            lock (_syncLock)
            {
                if (_memoryCache.ContainsKey(url)) _memoryCache.Remove(url);
                if (_bitmapCache.ContainsKey(url)) _bitmapCache.Remove(url);
                if (_avatarFileMap.ContainsKey(url))
                {
                    fileName = _avatarFileMap[url];
                    _avatarFileMap.Remove(url);
                }
            }

            if (!string.IsNullOrEmpty(fileName))
            {
                try
                {
                    var folder = await GetCacheFolderAsync();
                    if (folder != null)
                    {
                        var file = await folder.GetFileAsync(fileName);
                        if (file != null)
                        {
                            await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// 清理图片本地与内存缓存
        /// </summary>
        public static async Task ClearCacheAsync()
        {
            lock (_syncLock)
            {
                _memoryCache.Clear();
                _bitmapCache.Clear();
                _avatarFileMap.Clear();
                _knownCacheFiles.Clear();
                _cacheFolderScanned = false;
                _cacheFolder = null;
            }

            try
            {
                var folder = await GetCacheFolderAsync();
                if (folder != null)
                {
                    await folder.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }
            }
            catch { }
        }
    }
}
