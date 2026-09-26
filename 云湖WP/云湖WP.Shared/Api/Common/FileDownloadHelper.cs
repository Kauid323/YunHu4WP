using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Security.Cryptography.Certificates;
using Windows.Storage;
using Windows.Web.Http;
using Windows.Web.Http.Filters;
using 云湖WP.Utils;

namespace 云湖WP.Api.Common
{
    /// <summary>
    /// 文件下载助手 (智能存入系统公共 Music/Videos/Pictures 媒体目录、自动附带 Referer 防盗链请求头、忽略过时证书、支持流式实时下载进度通知)
    /// </summary>
    public static class FileDownloadHelper
    {
        private const string RefererUrl = "https://myapp.jwznb.com";
        private const string UserAgent = "Mozilla/5.0 (Windows Phone 8.1; ARM; Trident/7.0; Touch; rv:11.0; IEMobile/11.0; NOKIA; Lumia 930) like Gecko";

        /// <summary>
        /// 带实时进度报告的文件下载方法（根据类型直接存入系统公共 Music/Videos/Pictures 目录）
        /// </summary>
        /// <param name="fileUrl">文件下载直链</param>
        /// <param name="fileName">期望保存的文件名</param>
        /// <param name="expectedFileSize">预期文件大小 (字节)</param>
        /// <param name="progress">进度报告回调 (0 - 100)</param>
        /// <returns>下载成功后的 StorageFile 对象</returns>
        public static async Task<StorageFile> DownloadFileWithProgressAsync(string fileUrl, string fileName, long expectedFileSize = 0, IProgress<double> progress = null)
        {
            if (string.IsNullOrEmpty(fileUrl))
            {
                throw new ArgumentException("文件下载链接不能为空", "fileUrl");
            }

            string finalUrl = fileUrl.Trim();
            if (finalUrl.StartsWith("//"))
            {
                finalUrl = "https:" + finalUrl;
            }

            string safeFileName = fileName;
            if (string.IsNullOrEmpty(safeFileName))
            {
                try
                {
                    var uri = new Uri(finalUrl);
                    safeFileName = Path.GetFileName(uri.LocalPath);
                }
                catch { }
            }
            if (string.IsNullOrEmpty(safeFileName))
            {
                safeFileName = "yunhu_file_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            }

            // 1. 创建目标存储文件（根据类型存入系统公共对应目录）
            StorageFile targetFile = await CreateTargetStorageFileAsync(safeFileName);
            AppLogger.Log("FileDownload", string.Format("开始下载文件: Name={0}, TargetPath={1}, Url={2}", safeFileName, targetFile.Path, finalUrl));

            // 2. 配置带 WP8.1 证书忽略与 Referer 请求头的 HttpClient
            var filter = new HttpBaseProtocolFilter();
            filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Untrusted);
            filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.InvalidName);
            filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Expired);
            filter.AllowAutoRedirect = true;

            using (var client = new HttpClient(filter))
            {
                // 关键防盗链 Header: 云湖数据床地址需要 Referer: https://myapp.jwznb.com
                client.DefaultRequestHeaders.TryAppendWithoutValidation("Referer", RefererUrl);
                client.DefaultRequestHeaders.TryAppendWithoutValidation("User-Agent", UserAgent);

                var uri = new Uri(finalUrl);
                var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                ulong totalBytes = 0;
                if (response.Content != null && response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > 0)
                {
                    totalBytes = response.Content.Headers.ContentLength.Value;
                }
                else if (expectedFileSize > 0)
                {
                    totalBytes = (ulong)expectedFileSize;
                }

                // 3. 流式读取并实时更新进度
                using (var inputStream = (await response.Content.ReadAsInputStreamAsync()).AsStreamForRead())
                using (var outputStream = await targetFile.OpenStreamForWriteAsync())
                {
                    byte[] buffer = new byte[16384]; // 16 KB 块缓冲
                    ulong downloadedBytes = 0;
                    int bytesRead;

                    if (progress != null) progress.Report(1.0);

                    while ((bytesRead = await inputStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await outputStream.WriteAsync(buffer, 0, bytesRead);
                        downloadedBytes += (ulong)bytesRead;

                        if (totalBytes > 0 && progress != null)
                        {
                            double pct = Math.Min(100.0, (double)downloadedBytes * 100.0 / totalBytes);
                            progress.Report(pct);
                        }
                    }

                    await outputStream.FlushAsync();
                }

                if (progress != null) progress.Report(100.0);
                AppLogger.Log("FileDownload", "文件已成功下载存入: " + targetFile.Path);
                return targetFile;
            }
        }

        /// <summary>
        /// 创建目标存储文件（根据文件类型存入系统公共 Music / Videos / Pictures 目录，使自带应用、文件管理器和 USB 传输均可直接访问）
        /// </summary>
        private static async Task<StorageFile> CreateTargetStorageFileAsync(string fileName)
        {
            StorageFile file = null;
            string ext = Path.GetExtension(fileName).ToLowerInvariant();

            // 1. 音乐/音频类型保存至系统公共音乐库 (C:\Data\Users\Public\Music)
            if (ext == ".mp3" || ext == ".m4a" || ext == ".wav" || ext == ".aac" || ext == ".flac" || ext == ".ogg" || ext == ".wma" || ext == ".mid" || ext == ".midi")
            {
                try
                {
                    file = await KnownFolders.MusicLibrary.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
                    if (file != null)
                    {
                        AppLogger.Log("FileDownload", "成功在系统公共音乐库(Music)创建文件: " + file.Path);
                        return file;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log("FileDownload", "在公共 Music 创建文件失败: " + ex.Message);
                }
            }

            // 2. 视频类型保存至系统公共视频库 (C:\Data\Users\Public\Videos)
            if (ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".wmv" || ext == ".mov" || ext == ".3gp" || ext == ".flv" || ext == ".webm")
            {
                try
                {
                    file = await KnownFolders.VideosLibrary.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
                    if (file != null)
                    {
                        AppLogger.Log("FileDownload", "成功在系统公共视频库(Videos)创建文件: " + file.Path);
                        return file;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log("FileDownload", "在公共 Videos 创建文件失败: " + ex.Message);
                }
            }

            // 3. 图片类型保存至系统公共相册 (C:\Data\Users\Public\Pictures\Saved Pictures)
            if (ext == ".jpg" || ext == ".png" || ext == ".gif" || ext == ".jpeg" || ext == ".bmp" || ext == ".webp")
            {
                try
                {
                    file = await KnownFolders.SavedPictures.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
                    if (file != null)
                    {
                        AppLogger.Log("FileDownload", "成功在系统公共相册(Pictures)创建文件: " + file.Path);
                        return file;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log("FileDownload", "在公共 Pictures 创建文件失败: " + ex.Message);
                }
            }

            // 4. 其他类型普通文件（doc, pdf, zip 等）：优先保存在公共 Music/Pictures 目录中（这样文件管理器可以浏览到）
            try
            {
                file = await KnownFolders.MusicLibrary.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
                if (file != null)
                {
                    AppLogger.Log("FileDownload", "普通文件已存入公共 Music 目录: " + file.Path);
                    return file;
                }
            }
            catch { }

            try
            {
                file = await KnownFolders.SavedPictures.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
                if (file != null)
                {
                    AppLogger.Log("FileDownload", "普通文件已存入公共 Pictures 目录: " + file.Path);
                    return file;
                }
            }
            catch { }

            // 5. 最终回退到应用沙盒 LocalFolder
            if (file == null)
            {
                try
                {
                    file = await ApplicationData.Current.LocalFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
                    AppLogger.Log("FileDownload", "回退在应用沙盒 LocalFolder 创建文件: " + file.Path);
                }
                catch { }
            }

            return file;
        }
    }
}
