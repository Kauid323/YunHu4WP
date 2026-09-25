using System;
using System.Collections.Generic;
using System.ComponentModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Message
{
    /// <summary>
    /// 单条聊天消息展示模型 (支持数据绑定与 UI 状态通知)
    /// </summary>
    public class ChatMessageItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public string MsgId { get; set; }
        public string ChatId { get; set; }
        public int ChatType { get; set; } // 1-用户，2-群聊，3-机器人
        public long MsgSeq { get; set; }

        public string Direction { get; set; } // "left" 或 "right"
        public bool IsSelf
        {
            get { return string.Equals(Direction, "right", StringComparison.OrdinalIgnoreCase); }
        }

        public Visibility SelfBubbleVisibility
        {
            get { return IsSelf ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility OtherBubbleVisibility
        {
            get { return IsSelf ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility ShowSenderNameVisibility
        {
            get { return (ChatType == 2 && !IsSelf) ? Visibility.Visible : Visibility.Collapsed; }
        }

        public int ContentType { get; set; } // 1-文本, 2-图片, 3-Markdown, 4-文件, 7-表情, 8-HTML, 11-语音
        private string _text;
        public string Text
        {
            get { return _text; }
            set
            {
                if (_text != value)
                {
                    _text = value;
                    OnPropertyChanged("Text");
                }
            }
        }
        public string ImageUrl { get; set; }
        public string StickerUrl { get; set; }
        public string FileName { get; set; }
        public string FileUrl { get; set; }
        public long FileSize { get; set; }
        public string QuoteMsgText { get; set; }
        public string QuoteMsgId { get; set; }
        public string Tip { get; set; }

        public string FormattedFileSize
        {
            get
            {
                if (FileSize <= 0) return "";
                if (FileSize < 1024) return FileSize + " B";
                if (FileSize < 1024 * 1024) return (FileSize / 1024.0).ToString("F1") + " KB";
                if (FileSize < 1024 * 1024 * 1024) return (FileSize / (1024.0 * 1024.0)).ToString("F2") + " MB";
                return (FileSize / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
            }
        }

        public string DisplayFileName
        {
            get
            {
                if (!string.IsNullOrEmpty(FileName)) return FileName;
                if (!string.IsNullOrEmpty(FileUrl))
                {
                    try
                    {
                        var uri = new Uri(FileUrl);
                        string seg = System.IO.Path.GetFileName(uri.LocalPath);
                        if (!string.IsNullOrEmpty(seg)) return seg;
                    }
                    catch { }
                }
                return "文件";
            }
        }

        // 下载状态与进度绑定
        private bool _isDownloading;
        public bool IsDownloading
        {
            get { return _isDownloading; }
            set
            {
                if (_isDownloading != value)
                {
                    _isDownloading = value;
                    OnPropertyChanged("IsDownloading");
                    OnPropertyChanged("DownloadProgressVisibility");
                }
            }
        }

        private double _downloadProgress;
        public double DownloadProgress
        {
            get { return _downloadProgress; }
            set
            {
                if (Math.Abs(_downloadProgress - value) > 0.01)
                {
                    _downloadProgress = value;
                    OnPropertyChanged("DownloadProgress");
                }
            }
        }

        private string _downloadStatusText = "点击下载";
        public string DownloadStatusText
        {
            get { return string.IsNullOrEmpty(_downloadStatusText) ? "点击下载" : _downloadStatusText; }
            set
            {
                if (_downloadStatusText != value)
                {
                    _downloadStatusText = value;
                    OnPropertyChanged("DownloadStatusText");
                }
            }
        }

        private bool _isDownloaded;
        public bool IsDownloaded
        {
            get { return _isDownloaded; }
            set
            {
                if (_isDownloaded != value)
                {
                    _isDownloaded = value;
                    OnPropertyChanged("IsDownloaded");
                }
            }
        }

        public Visibility DownloadProgressVisibility
        {
            get { return IsDownloading ? Visibility.Visible : Visibility.Collapsed; }
        }

        // 发送者信息
        public string SenderId { get; set; }
        public string SenderName { get; set; }
        public string SenderAvatarUrl { get; set; }

        public string DisplaySenderName
        {
            get
            {
                if (!string.IsNullOrEmpty(SenderName)) return SenderName;
                if (!string.IsNullOrEmpty(SenderId)) return SenderId;
                return "云湖用户";
            }
        }

        public string SenderAvatarLetter
        {
            get
            {
                string name = DisplaySenderName;
                if (!string.IsNullOrEmpty(name))
                {
                    return name.Substring(0, 1).ToUpper();
                }
                return "云";
            }
        }

        private BitmapImage _senderAvatarBitmap;
        public BitmapImage SenderAvatarBitmap
        {
            get { return _senderAvatarBitmap; }
            set
            {
                if (_senderAvatarBitmap != value)
                {
                    _senderAvatarBitmap = value;
                    OnPropertyChanged("SenderAvatarBitmap");
                }
            }
        }

        private BitmapImage _imageBitmap;
        public BitmapImage ImageBitmap
        {
            get { return _imageBitmap; }
            set
            {
                if (_imageBitmap != value)
                {
                    _imageBitmap = value;
                    OnPropertyChanged("ImageBitmap");
                }
            }
        }

        /// <summary>
        /// 提取出的图片实际 URL
        /// </summary>
        public string ExtractedImageUrl
        {
            get
            {
                if (!string.IsNullOrEmpty(ImageUrl)) return ImageUrl.Trim();
                if (!string.IsNullOrEmpty(StickerUrl)) return StickerUrl.Trim();
                if (!string.IsNullOrEmpty(Text))
                {
                    string t = Text.Trim();
                    // Markdown: ![alt](url)
                    if (t.StartsWith("![") && t.Contains("](") && t.EndsWith(")"))
                    {
                        int start = t.IndexOf("](") + 2;
                        int end = t.LastIndexOf(")");
                        if (end > start)
                        {
                            return t.Substring(start, end - start).Trim();
                        }
                    }
                    // [image:url]
                    if (t.StartsWith("[image:") && t.EndsWith("]"))
                    {
                        return t.Substring(7, t.Length - 8).Trim();
                    }
                    // http url that ends with image extensions
                    if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        string lower = t.ToLower();
                        if (lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") || lower.EndsWith(".png") || lower.EndsWith(".gif") || lower.EndsWith(".webp") || lower.EndsWith(".bmp") || lower.Contains("/image/"))
                        {
                            return t;
                        }
                    }
                }
                return "";
            }
        }

        /// <summary>
        /// 是否为图片消息
        /// </summary>
        public bool IsImageMsg
        {
            get
            {
                return ContentType == 2 || ContentType == 7 || !string.IsNullOrEmpty(ExtractedImageUrl);
            }
        }

        /// <summary>
        /// 是否为文件消息
        /// </summary>
        public bool IsFileMsg
        {
            get
            {
                return ContentType == 4 || (!string.IsNullOrEmpty(FileUrl) && !IsImageMsg);
            }
        }

        public Visibility ImageMsgVisibility
        {
            get { return IsImageMsg ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility FileMsgVisibility
        {
            get { return IsFileMsg ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility TextMsgVisibility
        {
            get { return (!IsImageMsg && !IsFileMsg && !string.IsNullOrEmpty(Text)) ? Visibility.Visible : Visibility.Collapsed; }
        }

        public long SendTime { get; set; }

        public string FormattedTime
        {
            get
            {
                if (SendTime <= 0) return "";
                try
                {
                    var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    var dt = epoch.AddMilliseconds(SendTime).ToLocalTime();
                    var now = DateTime.Now;

                    if (dt.Date == now.Date)
                    {
                        return dt.ToString("HH:mm");
                    }
                    if (dt.Date == now.Date.AddDays(-1))
                    {
                        return "昨天 " + dt.ToString("HH:mm");
                    }
                    if (dt.Year == now.Year)
                    {
                        return dt.ToString("MM-dd HH:mm");
                    }
                    return dt.ToString("yyyy-MM-dd HH:mm");
                }
                catch
                {
                    return "";
                }
            }
        }
    }

    /// <summary>
    /// 图片预览页面导航参数
    /// </summary>
    public class ImageViewerNavArgs
    {
        public string ImageUrl { get; set; }
        public string Title { get; set; }
        public string FallbackLetter { get; set; }
    }

    /// <summary>
    /// 导航传递给 ChatPage 的参数模型
    /// </summary>
    public class ChatNavigationArgs
    {
        public string ChatId { get; set; }
        public int ChatType { get; set; }
        public string Title { get; set; }
        public string AvatarUrl { get; set; }
        public string Token { get; set; }
    }

    /// <summary>
    /// 消息列表获取结果 (/v1/msg/list-message-by-seq)
    /// </summary>
    public class MessageListResult : ApiResult
    {
        public List<ChatMessageItem> Messages { get; set; }
        public int Total { get; set; }

        public MessageListResult()
        {
            Messages = new List<ChatMessageItem>();
            Code = 1;
        }
    }

    /// <summary>
    /// 发送消息结果 (/v1/msg/send-message)
    /// </summary>
    public class SendMessageResult : ApiResult
    {
        public string MsgId { get; set; }
    }
}
