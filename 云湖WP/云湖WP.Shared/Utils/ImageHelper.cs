using System;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace 云湖WP.Utils
{
    /// <summary>
    /// 图片基础处理与 URL 格式化工具
    /// </summary>
    public static class ImageHelper
    {
        private const string DefaultImageHost = "https://chat-img.jwznb.com";

        /// <summary>
        /// Base64 字符串转 BitmapImage (用于验证码等)
        /// </summary>
        public static async Task<BitmapImage> Base64ToBitmapImageAsync(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;

            if (base64.Contains(","))
            {
                base64 = base64.Substring(base64.IndexOf(",") + 1);
            }
            base64 = base64.Trim();

            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                using (var stream = new InMemoryRandomAccessStream())
                {
                    using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                    {
                        writer.WriteBytes(bytes);
                        await writer.StoreAsync();
                    }
                    stream.Seek(0);
                    var image = new BitmapImage();
                    await image.SetSourceAsync(stream);
                    return image;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Base64ToBitmapImageAsync error: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 格式化七牛云缩略图/头像 URL (按需生成指定宽高的缩略图)
        /// 若 width <= 0 且 height <= 0，则返回原始无参纯净 URL
        /// </summary>
        public static string FormatQiniuUrl(string url, int width = 96, int height = 96)
        {
            if (string.IsNullOrEmpty(url)) return "";

            string trimmed = url.Trim();
            if (trimmed.Length == 0) return "";

            // 修正相对协议
            if (trimmed.StartsWith("//"))
            {
                trimmed = "https:" + trimmed;
            }

            // 补充相对路径的主机名前缀
            if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.StartsWith("/"))
                {
                    trimmed = DefaultImageHost + trimmed;
                }
                else
                {
                    trimmed = DefaultImageHost + "/" + trimmed;
                }
            }

            // 将 HTTP 的 chat-img 自动升级为 HTTPS 避免重定向与安全策略拦截
            if (trimmed.StartsWith("http://chat-img", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "https://" + trimmed.Substring("http://".Length);
            }

            // 若不需要缩放 (原图)，直接返回无参纯净 URL
            if (width <= 0 && height <= 0) return SafeEscapeUrl(trimmed);

            // 如果已经包含 imageView2 参数，不再重复拼接
            if (trimmed.IndexOf("imageView2/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return SafeEscapeUrl(trimmed);
            }

            // 避开默认头像路径
            if (trimmed.IndexOf("/default-avatars/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                trimmed.IndexOf("/defalut-avatars/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return SafeEscapeUrl(trimmed);
            }

            // 避开不支持七牛图片处理的后缀格式
            string lower = trimmed.ToLowerInvariant();
            string pathPart = lower.Contains("?") ? lower.Substring(0, lower.IndexOf('?')) : lower;
            if (pathPart.EndsWith(".tmp") || pathPart.EndsWith(".gif") || pathPart.EndsWith(".svg")
                || pathPart.EndsWith(".mp4") || pathPart.EndsWith(".mp3") || pathPart.EndsWith(".wav")
                || pathPart.EndsWith(".apk") || pathPart.EndsWith(".zip") || pathPart.EndsWith(".pdf")
                || pathPart.EndsWith(".doc") || pathPart.EndsWith(".docx"))
            {
                return SafeEscapeUrl(trimmed);
            }

            // 拼接七牛云 imageView2/2 参数 (96x96 / format/jpg / q75)
            string param = string.Format("imageView2/2/w/{0}/h/{1}/format/jpg/q/75", width, height);
            string finalUrl;

            if (trimmed.Contains("?"))
            {
                finalUrl = (trimmed.EndsWith("?") || trimmed.EndsWith("&")) ? trimmed + param : trimmed + "&" + param;
            }
            else
            {
                finalUrl = trimmed + "?" + param;
            }

            return SafeEscapeUrl(finalUrl);
        }

        /// <summary>
        /// 格式化大图查看器 URL:
        /// 1. 对于本身是 JPG / PNG / GIF / BMP 的图片：保持原始纯净 URL，绝不添加任何多余参数！
        /// 2. 对于 WebP 格式（Windows Phone 8.1 硬件不支持解码 WebP）：追加七牛 imageView2/0/format/jpg 在线转码为原分辨率 JPEG。
        /// </summary>
        public static string FormatFullImageUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            string trimmed = url.Trim();
            if (trimmed.Length == 0) return "";

            // 修正相对协议
            if (trimmed.StartsWith("//")) trimmed = "https:" + trimmed;
            if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.StartsWith("/") ? DefaultImageHost + trimmed : DefaultImageHost + "/" + trimmed;
            }

            // HTTP 升级为 HTTPS
            if (trimmed.StartsWith("http://chat-img", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "https://" + trimmed.Substring("http://".Length);
            }

            // 若已经带有 imageView2，直接返回
            if (trimmed.IndexOf("imageView2/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return SafeEscapeUrl(trimmed);
            }

            string lower = trimmed.ToLowerInvariant();
            string pathPart = lower.Contains("?") ? lower.Substring(0, lower.IndexOf('?')) : lower;

            // 1. 本身为 JPG, JPEG, PNG, BMP, GIF, SVG 等 WP8.1 原生支持格式：保持原始纯净链接，不添加任何参数！
            if (pathPart.EndsWith(".jpg") || pathPart.EndsWith(".jpeg") || pathPart.EndsWith(".png") ||
                pathPart.EndsWith(".bmp") || pathPart.EndsWith(".gif") || pathPart.EndsWith(".svg") ||
                pathPart.EndsWith(".ico") || pathPart.EndsWith(".mp4"))
            {
                return SafeEscapeUrl(trimmed);
            }

            // 2. 针对 WebP 格式（WP8.1 系统无 WebP 解码器）：
            // 在七牛图床 (包含 chat-img.jwznb.com, chat-img1, chat-img2, chat-img3 等所有 jwznb.com 域名) 上追加 imageView2/0/format/jpg
            bool isQiniu = lower.Contains("jwznb.com") || lower.Contains("qiniu") || lower.Contains("clouddn");
            bool isWebpOrExpression = pathPart.EndsWith(".webp") || pathPart.Contains("/expression/") || (!pathPart.Contains(".") && isQiniu);

            if (isQiniu && isWebpOrExpression)
            {
                string param = "imageView2/0/format/jpg/q/95";
                string finalUrl;
                if (trimmed.Contains("?"))
                {
                    finalUrl = (trimmed.EndsWith("?") || trimmed.EndsWith("&")) ? trimmed + param : trimmed + "&" + param;
                }
                else
                {
                    finalUrl = trimmed + "?" + param;
                }
                return SafeEscapeUrl(finalUrl);
            }

            return SafeEscapeUrl(trimmed);
        }

        /// <summary>
        /// 将 WebP 链接转码为 JPEG 链接 (专门用于在 WP8.1 上展示 WebP 表情/贴纸/图片)
        /// </summary>
        public static string ConvertWebPToJpgUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            string trimmed = url.Trim();
            if (trimmed.StartsWith("//")) trimmed = "https:" + trimmed;
            if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.StartsWith("/") ? DefaultImageHost + trimmed : DefaultImageHost + "/" + trimmed;
            }

            if (trimmed.StartsWith("http://chat-img", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "https://" + trimmed.Substring("http://".Length);
            }

            if (trimmed.IndexOf("imageView2/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return SafeEscapeUrl(trimmed);
            }

            string param = "imageView2/0/format/jpg/q/95";
            string finalUrl;
            if (trimmed.Contains("?"))
            {
                finalUrl = (trimmed.EndsWith("?") || trimmed.EndsWith("&")) ? trimmed + param : trimmed + "&" + param;
            }
            else
            {
                finalUrl = trimmed + "?" + param;
            }
            return SafeEscapeUrl(finalUrl);
        }

        private const string DefaultVideoHost = "https://chat-video1.jwznb.com";
        private const string DefaultFileHost = "https://chat-file.jwznb.com";

        /// <summary>
        /// 格式化七牛云视频 URL (支持自动补全 https://chat-video1.jwznb.com 前缀)
        /// </summary>
        public static string FormatVideoUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            string trimmed = url.Trim();
            if (trimmed.Length == 0) return "";
            if (trimmed.StartsWith("//")) trimmed = "https:" + trimmed;
            if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.StartsWith("/")) trimmed = DefaultVideoHost + trimmed;
                else trimmed = DefaultVideoHost + "/" + trimmed;
            }
            return SafeEscapeUrl(trimmed);
        }

        /// <summary>
        /// 格式化七牛云文件下载 URL (支持自动补全 https://chat-file.jwznb.com 前缀)
        /// </summary>
        public static string FormatFileUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            string trimmed = url.Trim();
            if (trimmed.Length == 0) return "";
            if (trimmed.StartsWith("//")) trimmed = "https:" + trimmed;
            if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.StartsWith("/")) trimmed = DefaultFileHost + trimmed;
                else trimmed = DefaultFileHost + "/" + trimmed;
            }
            return SafeEscapeUrl(trimmed);
        }

        private static string SafeEscapeUrl(string rawUrl)
        {
            if (string.IsNullOrEmpty(rawUrl)) return "";
            try
            {
                return Uri.EscapeUriString(rawUrl);
            }
            catch
            {
                return rawUrl.Replace(" ", "%20");
            }
        }
    }
}
