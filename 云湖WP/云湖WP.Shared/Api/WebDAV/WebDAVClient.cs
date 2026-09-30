using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Storage;
using 云湖WP.Api.Common;
using 云湖WP.Utils;

namespace 云湖WP.Api.WebDAV
{
    /// <summary>
    /// WebDAV 客户端 (PROPFIND, GET, PUT, MKCOL, DELETE 支持 Basic 认证与流式下载/上传进度)
    /// </summary>
    public static class WebDAVClient
    {
        public static async Task<List<WebDAVFile>> ListFilesAsync(WebDAVMountSetting mount, string relativePath = "")
        {
            var files = new List<WebDAVFile>();
            if (mount == null || string.IsNullOrEmpty(mount.WebdavUrl)) return files;

            try
            {
                string baseUrl = mount.WebdavUrl.TrimEnd('/');
                string rootPath = (mount.WebdavRootPath ?? "").Trim('/');
                string rel = (relativePath ?? "").Trim('/');

                string defaultPath = string.IsNullOrEmpty(rootPath) ? baseUrl : (baseUrl + "/" + rootPath);
                string fullUrl = string.IsNullOrEmpty(rel) ? defaultPath : (defaultPath + "/" + rel);

                // 规范化 URL 避免双斜杠
                fullUrl = NormalizeUrl(fullUrl);
                string currentPath = string.IsNullOrEmpty(rootPath) ? ("/" + rel) : ("/" + rootPath + "/" + rel);
                currentPath = "/" + currentPath.Trim('/');

                AppLogger.Log("WebDAVClient", "PROPFIND 目标: " + fullUrl + ", 相对路径: " + currentPath);

                var req = (HttpWebRequest)WebRequest.Create(new Uri(fullUrl));
                req.Method = "PROPFIND";
                req.Headers["Depth"] = "1";
                if (!string.IsNullOrEmpty(mount.WebdavUserName))
                {
                    string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(mount.WebdavUserName + ":" + (mount.WebdavPassword ?? "")));
                    req.Headers["Authorization"] = "Basic " + auth;
                }
                req.ContentType = "application/xml; charset=utf-8";

                byte[] body = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:displayname/><d:resourcetype/><d:getcontentlength/><d:getlastmodified/></d:prop></d:propfind>");
                using (var reqStream = await req.GetRequestStreamAsync())
                {
                    await reqStream.WriteAsync(body, 0, body.Length);
                }

                string xml = "";
                using (var resp = (HttpWebResponse)await req.GetResponseAsync())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    xml = await reader.ReadToEndAsync();
                }

                if (!string.IsNullOrEmpty(xml))
                {
                    files = ParsePropfindResponse(xml, mount, currentPath);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("WebDAVClient", "ListFilesAsync exception: " + ex.Message);
            }

            return files;
        }

        private static List<WebDAVFile> ParsePropfindResponse(string xml, WebDAVMountSetting mount, string currentPath)
        {
            var files = new List<WebDAVFile>();
            try
            {
                var doc = XDocument.Parse(xml);
                var responses = doc.Descendants().Where(e => e.Name.LocalName.Equals("response", StringComparison.OrdinalIgnoreCase));

                string normCurrent = "/" + currentPath.Trim('/') + "/";
                if (normCurrent == "//") normCurrent = "/";

                foreach (var res in responses)
                {
                    var hrefEl = res.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("href", StringComparison.OrdinalIgnoreCase));
                    if (hrefEl == null || string.IsNullOrEmpty(hrefEl.Value)) continue;

                    string rawHref = hrefEl.Value.Trim();
                    string decodedHref = WebUtility.UrlDecode(rawHref);

                    // 规范化 href 为绝对路径部分
                    string pathOnly = decodedHref;
                    if (pathOnly.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || pathOnly.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var u = new Uri(pathOnly);
                            pathOnly = u.AbsolutePath;
                        }
                        catch { }
                    }

                    pathOnly = "/" + pathOnly.Trim('/');
                    string trimmedCurrent = "/" + currentPath.Trim('/');

                    // 如果是当前目录本身，跳过
                    if (pathOnly.Equals(trimmedCurrent, StringComparison.OrdinalIgnoreCase) || pathOnly == "/" && trimmedCurrent == "/")
                    {
                        continue;
                    }

                    var resTypeEl = res.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("resourcetype", StringComparison.OrdinalIgnoreCase));
                    bool isDirectory = resTypeEl != null && resTypeEl.Descendants().Any(e => e.Name.LocalName.Equals("collection", StringComparison.OrdinalIgnoreCase));

                    long size = 0;
                    var lenEl = res.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("getcontentlength", StringComparison.OrdinalIgnoreCase));
                    if (lenEl != null)
                    {
                        long.TryParse(lenEl.Value, out size);
                    }

                    DateTime? lastMod = null;
                    var modEl = res.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("getlastmodified", StringComparison.OrdinalIgnoreCase));
                    if (modEl != null && !string.IsNullOrEmpty(modEl.Value))
                    {
                        DateTime dt;
                        if (DateTime.TryParse(modEl.Value, out dt)) lastMod = dt.ToLocalTime();
                    }

                    string name = pathOnly.Substring(pathOnly.LastIndexOf('/') + 1);
                    if (string.IsNullOrEmpty(name)) continue;

                    files.Add(new WebDAVFile
                    {
                        Name = name,
                        Path = pathOnly,
                        IsDirectory = isDirectory,
                        Size = size,
                        LastModified = lastMod,
                        MountSetting = mount
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("WebDAVClient", "ParsePropfindResponse exception: " + ex.Message);
            }
            return files;
        }

        public static Task<StorageFile> DownloadFileAsync(WebDAVMountSetting mount, WebDAVFile file)
        {
            return DownloadFileAsync(mount, file, null, CancellationToken.None);
        }

        public static Task<StorageFile> DownloadFileAsync(WebDAVMountSetting mount, WebDAVFile file, IProgress<double> progress)
        {
            return DownloadFileAsync(mount, file, progress, CancellationToken.None);
        }

        public static async Task<StorageFile> DownloadFileAsync(WebDAVMountSetting mount, WebDAVFile file, IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (mount == null || file == null) return null;

            string fullUrl = file.Path;
            if (!fullUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !fullUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                string baseUrl = mount.WebdavUrl.TrimEnd('/');
                fullUrl = baseUrl + "/" + file.Path.TrimStart('/');
            }
            fullUrl = NormalizeUrl(fullUrl);

            AppLogger.Log("WebDAVClient", "开始下载 WebDAV 文件: Name=" + file.Name + ", Url=" + fullUrl);

            StorageFile targetFile = await CreateDownloadTargetFileAsync(file.Name);

            var req = (HttpWebRequest)WebRequest.Create(new Uri(fullUrl));
            req.Method = "GET";
            if (!string.IsNullOrEmpty(mount.WebdavUserName))
            {
                string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(mount.WebdavUserName + ":" + (mount.WebdavPassword ?? "")));
                req.Headers["Authorization"] = "Basic " + auth;
            }

            using (cancellationToken.Register(() => { try { req.Abort(); } catch { } }))
            using (var resp = (HttpWebResponse)await req.GetResponseAsync())
            using (var inStream = resp.GetResponseStream())
            using (var outStream = await targetFile.OpenStreamForWriteAsync())
            {
                long totalBytes = resp.ContentLength > 0 ? resp.ContentLength : file.Size;
                byte[] buffer = new byte[16384];
                long downloaded = 0;
                int read;

                if (progress != null) progress.Report(1.0);

                while ((read = await inStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await outStream.WriteAsync(buffer, 0, read);
                    downloaded += read;
                    if (totalBytes > 0 && progress != null)
                    {
                        double pct = Math.Min(100.0, (double)downloaded * 100.0 / totalBytes);
                        progress.Report(pct);
                    }
                }
                await outStream.FlushAsync();
            }

            if (progress != null) progress.Report(100.0);
            AppLogger.Log("WebDAVClient", "WebDAV 文件已成功下载存入: " + targetFile.Path);
            return targetFile;
        }

        public static Task<bool> UploadFileAsync(WebDAVMountSetting mount, StorageFile localFile, string remoteRelativeDir)
        {
            return UploadFileAsync(mount, localFile, remoteRelativeDir, null, CancellationToken.None);
        }

        public static Task<bool> UploadFileAsync(WebDAVMountSetting mount, StorageFile localFile, string remoteRelativeDir, IProgress<double> progress)
        {
            return UploadFileAsync(mount, localFile, remoteRelativeDir, progress, CancellationToken.None);
        }

        public static async Task<bool> UploadFileAsync(WebDAVMountSetting mount, StorageFile localFile, string remoteRelativeDir, IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (mount == null || localFile == null) return false;

            string baseUrl = mount.WebdavUrl.TrimEnd('/');
            string rootPath = (mount.WebdavRootPath ?? "").Trim('/');
            string rel = (remoteRelativeDir ?? "").Trim('/');

            string defaultPath = string.IsNullOrEmpty(rootPath) ? baseUrl : (baseUrl + "/" + rootPath);
            string dirUrl = string.IsNullOrEmpty(rel) ? defaultPath : (defaultPath + "/" + rel);
            string fullUrl = NormalizeUrl(dirUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(localFile.Name));

            AppLogger.Log("WebDAVClient", "开始上传 WebDAV 文件: " + localFile.Name + " -> " + fullUrl);

            var props = await localFile.GetBasicPropertiesAsync();
            long fileSize = (long)props.Size;

            var req = (HttpWebRequest)WebRequest.Create(new Uri(fullUrl));
            req.Method = "PUT";
            req.ContentType = "application/octet-stream";
            if (!string.IsNullOrEmpty(mount.WebdavUserName))
            {
                string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(mount.WebdavUserName + ":" + (mount.WebdavPassword ?? "")));
                req.Headers["Authorization"] = "Basic " + auth;
            }

            if (progress != null) progress.Report(5.0);

            using (cancellationToken.Register(() => { try { req.Abort(); } catch { } }))
            using (var reqStream = await req.GetRequestStreamAsync())
            using (var fileStream = await localFile.OpenStreamForReadAsync())
            {
                byte[] buffer = new byte[32768];
                long uploaded = 0;
                int read;

                while ((read = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await reqStream.WriteAsync(buffer, 0, read);
                    uploaded += read;
                    if (fileSize > 0 && progress != null)
                    {
                        double pct = 5.0 + Math.Min(93.0, (double)uploaded * 93.0 / fileSize);
                        progress.Report(pct);
                    }
                }
                await reqStream.FlushAsync();
            }

            using (var resp = (HttpWebResponse)await req.GetResponseAsync())
            {
                if (progress != null) progress.Report(100.0);
                return (int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300;
            }
        }

        public static async Task<bool> CreateFolderAsync(WebDAVMountSetting mount, string remoteRelativeDir, string newFolderName)
        {
            if (mount == null || string.IsNullOrWhiteSpace(newFolderName)) return false;

            string baseUrl = mount.WebdavUrl.TrimEnd('/');
            string rootPath = (mount.WebdavRootPath ?? "").Trim('/');
            string rel = (remoteRelativeDir ?? "").Trim('/');

            string defaultPath = string.IsNullOrEmpty(rootPath) ? baseUrl : (baseUrl + "/" + rootPath);
            string dirUrl = string.IsNullOrEmpty(rel) ? defaultPath : (defaultPath + "/" + rel);
            string fullUrl = NormalizeUrl(dirUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(newFolderName.Trim()) + "/");

            AppLogger.Log("WebDAVClient", "MKCOL 创建文件夹: " + fullUrl);

            var req = (HttpWebRequest)WebRequest.Create(new Uri(fullUrl));
            req.Method = "MKCOL";
            if (!string.IsNullOrEmpty(mount.WebdavUserName))
            {
                string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(mount.WebdavUserName + ":" + (mount.WebdavPassword ?? "")));
                req.Headers["Authorization"] = "Basic " + auth;
            }

            using (var resp = (HttpWebResponse)await req.GetResponseAsync())
            {
                return (int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300;
            }
        }

        public static async Task<bool> DeleteAsync(WebDAVMountSetting mount, string remotePath)
        {
            if (mount == null || string.IsNullOrEmpty(remotePath)) return false;

            string fullUrl = remotePath;
            if (!fullUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !fullUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                string baseUrl = mount.WebdavUrl.TrimEnd('/');
                fullUrl = baseUrl + "/" + remotePath.TrimStart('/');
            }
            fullUrl = NormalizeUrl(fullUrl);

            AppLogger.Log("WebDAVClient", "DELETE 删除 WebDAV 资源: " + fullUrl);

            var req = (HttpWebRequest)WebRequest.Create(new Uri(fullUrl));
            req.Method = "DELETE";
            if (!string.IsNullOrEmpty(mount.WebdavUserName))
            {
                string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(mount.WebdavUserName + ":" + (mount.WebdavPassword ?? "")));
                req.Headers["Authorization"] = "Basic " + auth;
            }

            using (var resp = (HttpWebResponse)await req.GetResponseAsync())
            {
                return (int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300;
            }
        }

        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            string scheme = "";
            string rest = url;
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                scheme = "https://";
                rest = url.Substring("https://".Length);
            }
            else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                scheme = "http://";
                rest = url.Substring("http://".Length);
            }
            while (rest.Contains("//")) rest = rest.Replace("//", "/");
            return scheme + rest;
        }

        private static async Task<StorageFile> CreateDownloadTargetFileAsync(string fileName)
        {
            StorageFile file = null;
            string ext = Path.GetExtension(fileName).ToLowerInvariant();

            if (ext == ".mp3" || ext == ".m4a" || ext == ".wav" || ext == ".flac" || ext == ".aac")
            {
                try { file = await KnownFolders.MusicLibrary.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName); } catch { }
            }
            else if (ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".mov")
            {
                try { file = await KnownFolders.VideosLibrary.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName); } catch { }
            }
            else if (ext == ".jpg" || ext == ".png" || ext == ".gif" || ext == ".jpeg" || ext == ".bmp" || ext == ".webp")
            {
                try { file = await KnownFolders.SavedPictures.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName); } catch { }
            }

            if (file == null)
            {
                try { file = await KnownFolders.MusicLibrary.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName); } catch { }
            }
            if (file == null)
            {
                try { file = await KnownFolders.SavedPictures.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName); } catch { }
            }
            if (file == null)
            {
                file = await ApplicationData.Current.LocalFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
            }
            return file;
        }
    }
}
