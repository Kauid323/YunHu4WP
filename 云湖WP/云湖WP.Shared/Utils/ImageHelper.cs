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
        /// 格式化七牛云图片 URL，按需拼接 imageView2 缩放/压缩参数
        /// 针对 96x96 头像生成 imageView2/2/w/96/h/96/q/75
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
            if (trimmed.StartsWith("http://chat-img.jwznb.com", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "https://" + trimmed.Substring("http://".Length);
            }

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
            string lower = trimmed.ToLower();
            string pathPart = lower.Contains("?") ? lower.Substring(0, lower.IndexOf('?')) : lower;
            if (pathPart.EndsWith(".tmp") || pathPart.EndsWith(".gif") || pathPart.EndsWith(".svg")
                || pathPart.EndsWith(".mp4") || pathPart.EndsWith(".mp3") || pathPart.EndsWith(".wav")
                || pathPart.EndsWith(".apk") || pathPart.EndsWith(".zip") || pathPart.EndsWith(".pdf")
                || pathPart.EndsWith(".doc") || pathPart.EndsWith(".docx"))
            {
                return SafeEscapeUrl(trimmed);
            }

            // 拼接七牛云 imageView2/2 参数 (96x96 / q75)
            string param = string.Format("imageView2/2/w/{0}/h/{1}/q/75", width, height);
            string finalUrl;

            if (trimmed.Contains("?"))
            {
                if (trimmed.EndsWith("?") || trimmed.EndsWith("&"))
                {
                    finalUrl = trimmed + param;
                }
                else
                {
                    finalUrl = trimmed + "&" + param;
                }
            }
            else
            {
                finalUrl = trimmed + "?" + param;
            }

            return SafeEscapeUrl(finalUrl);
        }

        private static string SafeEscapeUrl(string rawUrl)
        {
            if (string.IsNullOrEmpty(rawUrl)) return "";
            try
            {
                // 处理 URL 中的空格等未编码字符
                return Uri.EscapeUriString(rawUrl);
            }
            catch
            {
                return rawUrl.Replace(" ", "%20");
            }
        }
    }
}
