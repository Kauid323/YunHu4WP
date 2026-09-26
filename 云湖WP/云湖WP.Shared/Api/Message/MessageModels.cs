using System;
using System.Collections.Generic;
using System.ComponentModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;
using 云湖WP.Api.Common;
using 云湖WP.Utils;

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
        public string ChatTitle { get; set; }
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

        public int ContentType { get; set; } // 1-文本, 2-图片, 3-Markdown, 4-文件, 5-视频, 7-表情, 8-HTML, 11-语音
        private string _text;
        public string Text
        {
            get { return _text; }
            set
            {
                if (_text != value)
                {
                    _text = value;
                    _parsed = false;
                    OnPropertyChanged("Text");
                }
            }
        }
        public string ImageUrl { get; set; }
        public string StickerUrl { get; set; }
        public string FileName { get; set; }
        public string FileUrl { get; set; }
        public long FileSize { get; set; }
        public string VideoUrl { get; set; }
        public int VideoDuration { get; set; }
        public string QuoteVideoUrl { get; set; }
        public int QuoteVideoDuration { get; set; }
        public int MediaWidth { get; set; }
        public int MediaHeight { get; set; }
        public string QuoteMsgText { get; set; }
        public string QuoteMsgId { get; set; }
        public string AudioUrl { get; set; }
        public int AudioDuration { get; set; }
        public bool IsEdited { get; set; }
        public string Tip { get; set; }

        // 缓存预解析字段 (消除每次 UI 绘制与滚动时的重复字符串解析和内存分配)
        private bool _parsed = false;
        private string _extractedImageUrl = "";
        private string _extractedVideoUrl = "";
        private bool _isVideoMsg;
        private bool _isImageMsg;
        private bool _isFileMsg;
        private bool _isHtmlMsg;
        private Visibility _videoMsgVisibility = Visibility.Collapsed;
        private Visibility _imageMsgVisibility = Visibility.Collapsed;
        private Visibility _fileMsgVisibility = Visibility.Collapsed;
        private Visibility _htmlMsgVisibility = Visibility.Collapsed;
        private Visibility _textMsgVisibility = Visibility.Visible;
        private string _formattedVideoDuration = "";
        private string _formattedFileSize = "";
        private string _displayFileName = "文件";
        private string _displaySenderName = "云湖用户";
        private string _senderAvatarLetter = "云";
        private string _formattedTime = "";
        private string _displayText = "";

        public string DisplayText
        {
            get { if (!_parsed) InitParsedData(); return _displayText; }
        }

        public void InitParsedData()
        {
            // 1. 发送者与头像占位符
            if (!string.IsNullOrEmpty(SenderName)) _displaySenderName = SenderName;
            else if (!string.IsNullOrEmpty(SenderId)) _displaySenderName = SenderId;
            else _displaySenderName = "云湖用户";

            if (!string.IsNullOrEmpty(_displaySenderName))
                _senderAvatarLetter = _displaySenderName.Substring(0, 1).ToUpper();
            else
                _senderAvatarLetter = "云";

            // 2. 文件大小与名称
            if (FileSize <= 0) _formattedFileSize = "";
            else if (FileSize < 1024) _formattedFileSize = FileSize + " B";
            else if (FileSize < 1024 * 1024) _formattedFileSize = (FileSize / 1024.0).ToString("F1") + " KB";
            else if (FileSize < 1024 * 1024 * 1024) _formattedFileSize = (FileSize / (1024.0 * 1024.0)).ToString("F2") + " MB";
            else _formattedFileSize = (FileSize / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";

            if (!string.IsNullOrEmpty(FileName)) _displayFileName = FileName;
            else if (!string.IsNullOrEmpty(FileUrl))
            {
                try
                {
                    var uri = new Uri(FileUrl);
                    string seg = System.IO.Path.GetFileName(uri.LocalPath);
                    _displayFileName = !string.IsNullOrEmpty(seg) ? seg : "文件";
                }
                catch { _displayFileName = "文件"; }
            }
            else _displayFileName = "文件";

            // 3. 视频直链识别
            _extractedVideoUrl = "";
            if (!string.IsNullOrEmpty(VideoUrl)) _extractedVideoUrl = VideoUrl.Trim();
            else if (!string.IsNullOrEmpty(QuoteVideoUrl)) _extractedVideoUrl = QuoteVideoUrl.Trim();
            else if (ContentType == 10)
            {
                if (!string.IsNullOrEmpty(FileUrl)) _extractedVideoUrl = FileUrl.Trim();
                else if (!string.IsNullOrEmpty(ImageUrl)) _extractedVideoUrl = ImageUrl.Trim();
                else if (!string.IsNullOrEmpty(Text)) _extractedVideoUrl = Text.Trim();
            }
            else if (!string.IsNullOrEmpty(FileUrl))
            {
                string f = FileUrl.Trim().ToLower();
                if (f.EndsWith(".mp4") || f.EndsWith(".mov") || f.EndsWith(".avi") || 
                    f.EndsWith(".mkv") || f.EndsWith(".flv") || f.EndsWith(".3gp") || 
                    f.EndsWith(".wmv") || f.EndsWith(".webm") || f.EndsWith(".m4v") || f.Contains("/video/"))
                {
                    _extractedVideoUrl = FileUrl.Trim();
                }
            }
            else if (!string.IsNullOrEmpty(Text))
            {
                string t = Text.Trim();
                if ((t.StartsWith("![") || t.StartsWith("[")) && t.Contains("](") && t.EndsWith(")"))
                {
                    int titleEnd = t.IndexOf("](");
                    string title = t.Substring(0, titleEnd).ToLower();
                    if (title.Contains("video") || title.Contains("视频"))
                    {
                        int start = titleEnd + 2;
                        int end = t.LastIndexOf(")");
                        if (end > start) _extractedVideoUrl = t.Substring(start, end - start).Trim();
                    }
                }
                else if (t.StartsWith("[video:", StringComparison.OrdinalIgnoreCase) && t.EndsWith("]"))
                {
                    _extractedVideoUrl = t.Substring(7, t.Length - 8).Trim();
                }
                else if (t.StartsWith("<video", StringComparison.OrdinalIgnoreCase) && t.Contains("src=\""))
                {
                    int srcStart = t.IndexOf("src=\"", StringComparison.OrdinalIgnoreCase) + 5;
                    int srcEnd = t.IndexOf("\"", srcStart);
                    if (srcEnd > srcStart) _extractedVideoUrl = t.Substring(srcStart, srcEnd - srcStart).Trim();
                }
                else if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    string lower = t.ToLower();
                    if (lower.EndsWith(".mp4") || lower.EndsWith(".mov") || lower.EndsWith(".avi") || 
                        lower.EndsWith(".mkv") || lower.EndsWith(".flv") || lower.EndsWith(".3gp") || 
                        lower.EndsWith(".wmv") || lower.EndsWith(".webm") || lower.EndsWith(".m4v") || lower.Contains("/video/"))
                    {
                        _extractedVideoUrl = t;
                    }
                }
            }

            if (!string.IsNullOrEmpty(_extractedVideoUrl))
            {
                _extractedVideoUrl = ImageHelper.FormatVideoUrl(_extractedVideoUrl);
            }

            _isVideoMsg = ContentType == 10 || !string.IsNullOrEmpty(VideoUrl) || !string.IsNullOrEmpty(_extractedVideoUrl);

            // 4. 图片直链识别
            _extractedImageUrl = "";
            if (!_isVideoMsg)
            {
                if (!string.IsNullOrEmpty(ImageUrl)) _extractedImageUrl = ImageUrl.Trim();
                else if (!string.IsNullOrEmpty(StickerUrl)) _extractedImageUrl = StickerUrl.Trim();
                else if (!string.IsNullOrEmpty(Text))
                {
                    string t = Text.Trim();
                    if (t.StartsWith("![") && t.Contains("](") && t.EndsWith(")"))
                    {
                        int start = t.IndexOf("](") + 2;
                        int end = t.LastIndexOf(")");
                        if (end > start) _extractedImageUrl = t.Substring(start, end - start).Trim();
                    }
                    else if (t.StartsWith("[image:") && t.EndsWith("]"))
                    {
                        _extractedImageUrl = t.Substring(7, t.Length - 8).Trim();
                    }
                    else if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        string lower = t.ToLower();
                        if (lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") || lower.EndsWith(".png") || lower.EndsWith(".gif") || lower.EndsWith(".webp") || lower.EndsWith(".bmp") || lower.Contains("/image/"))
                        {
                            _extractedImageUrl = t;
                        }
                    }
                }
            }

            _isImageMsg = !_isVideoMsg && (ContentType == 2 || ContentType == 7 || !string.IsNullOrEmpty(_extractedImageUrl));
            _isFileMsg = !_isVideoMsg && !_isImageMsg && (ContentType == 4 || !string.IsNullOrEmpty(FileUrl));

            // 4.5. HTML 富文本识别 (ContentType == 8 或以典型 HTML 标签/文档头起始)
            bool looksLikeHtml = (ContentType == 8);
            if (!looksLikeHtml && !_isVideoMsg && !_isImageMsg && !_isFileMsg && !string.IsNullOrEmpty(Text))
            {
                string tTrim = Text.Trim();
                if (tTrim.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
                    tTrim.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
                    tTrim.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
                    (tTrim.StartsWith("<div", StringComparison.OrdinalIgnoreCase) && tTrim.Contains("</div>")) ||
                    (tTrim.StartsWith("<table", StringComparison.OrdinalIgnoreCase) && tTrim.Contains("</table>")) ||
                    (tTrim.StartsWith("<body", StringComparison.OrdinalIgnoreCase) && tTrim.Contains("</body>")) ||
                    (tTrim.StartsWith("<head", StringComparison.OrdinalIgnoreCase) && tTrim.Contains("</head>")) ||
                    (tTrim.StartsWith("<style", StringComparison.OrdinalIgnoreCase) && tTrim.Contains("</style>")) ||
                    (tTrim.StartsWith("<script", StringComparison.OrdinalIgnoreCase) && tTrim.Contains("</script>")))
                {
                    looksLikeHtml = true;
                }
            }

            _isHtmlMsg = !_isVideoMsg && !_isImageMsg && !_isFileMsg && looksLikeHtml;

            _videoMsgVisibility = _isVideoMsg ? Visibility.Visible : Visibility.Collapsed;
            _imageMsgVisibility = _isImageMsg ? Visibility.Visible : Visibility.Collapsed;
            _fileMsgVisibility = _isFileMsg ? Visibility.Visible : Visibility.Collapsed;
            _htmlMsgVisibility = _isHtmlMsg ? Visibility.Visible : Visibility.Collapsed;
            _textMsgVisibility = (!_isVideoMsg && !_isImageMsg && !_isFileMsg && !_isHtmlMsg && !string.IsNullOrEmpty(Text)) ? Visibility.Visible : Visibility.Collapsed;

            // 5. 视频时长与时间格式化
            if (VideoDuration > 0)
            {
                int mins = VideoDuration / 60;
                int secs = VideoDuration % 60;
                _formattedVideoDuration = string.Format("{0:D2}:{1:D2}", mins, secs);
            }
            else
            {
                _formattedVideoDuration = "";
            }

            if (SendTime > 0)
            {
                try
                {
                    var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    var dt = epoch.AddMilliseconds(SendTime).ToLocalTime();
                    var now = DateTime.Now;
                    if (dt.Date == now.Date) _formattedTime = dt.ToString("HH:mm");
                    else if (dt.Date == now.Date.AddDays(-1)) _formattedTime = "昨天 " + dt.ToString("HH:mm");
                    else if (dt.Year == now.Year) _formattedTime = dt.ToString("MM-dd HH:mm");
                    else _formattedTime = dt.ToString("yyyy-MM-dd HH:mm");
                }
                catch { _formattedTime = ""; }
            }
            else _formattedTime = "";

            // 6. 气泡长文本限制 (防止 50KB+ HTML/代码大段文本导致 XAML TextBlock 测量卡顿与 Direct3D 纹理重绘闪烁)
            if (string.IsNullOrEmpty(Text))
            {
                _displayText = "";
            }
            else if (Text.Length > 1000)
            {
                _displayText = Text.Substring(0, 1000) + "\n\n[长文本内容，点击气泡查看全文]";
            }
            else
            {
                _displayText = Text;
            }

            _parsed = true;
        }

        public string FormattedFileSize
        {
            get { if (!_parsed) InitParsedData(); return _formattedFileSize; }
        }

        public string DisplayFileName
        {
            get { if (!_parsed) InitParsedData(); return _displayFileName; }
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
            get { if (!_parsed) InitParsedData(); return _displaySenderName; }
        }

        public string SenderAvatarLetter
        {
            get { if (!_parsed) InitParsedData(); return _senderAvatarLetter; }
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
                    OnPropertyChanged("AvatarLetterVisibility");
                }
            }
        }

        public Visibility AvatarLetterVisibility
        {
            get { return _senderAvatarBitmap != null ? Visibility.Collapsed : Visibility.Visible; }
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

        public string ExtractedVideoUrl
        {
            get { if (!_parsed) InitParsedData(); return _extractedVideoUrl ?? ""; }
        }

        public bool IsVideoMsg
        {
            get { if (!_parsed) InitParsedData(); return _isVideoMsg; }
        }

        public Visibility VideoMsgVisibility
        {
            get { if (!_parsed) InitParsedData(); return _videoMsgVisibility; }
        }

        public string FormattedVideoDuration
        {
            get { if (!_parsed) InitParsedData(); return _formattedVideoDuration; }
        }

        public string ExtractedImageUrl
        {
            get { if (!_parsed) InitParsedData(); return _extractedImageUrl ?? ""; }
        }

        public bool IsImageMsg
        {
            get { if (!_parsed) InitParsedData(); return _isImageMsg; }
        }

        public bool IsFileMsg
        {
            get { if (!_parsed) InitParsedData(); return _isFileMsg; }
        }

        public Visibility ImageMsgVisibility
        {
            get { if (!_parsed) InitParsedData(); return _imageMsgVisibility; }
        }

        public Visibility FileMsgVisibility
        {
            get { if (!_parsed) InitParsedData(); return _fileMsgVisibility; }
        }

        public bool IsHtmlMsg
        {
            get { if (!_parsed) InitParsedData(); return _isHtmlMsg; }
        }

        public Visibility HtmlMsgVisibility
        {
            get { if (!_parsed) InitParsedData(); return _htmlMsgVisibility; }
        }

        public Visibility TextMsgVisibility
        {
            get { if (!_parsed) InitParsedData(); return _textMsgVisibility; }
        }

        public long SendTime { get; set; }

        public string FormattedTime
        {
            get { if (!_parsed) InitParsedData(); return _formattedTime; }
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
