using System;
using System.Collections.Generic;
using System.ComponentModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Conversation
{
    /// <summary>
    /// 会话类型枚举 (1-用户，2-群聊，3-机器人)
    /// </summary>
    public enum ChatType
    {
        Unknown = 0,
        User = 1,
        Group = 2,
        Bot = 3
    }

    /// <summary>
    /// 云湖会话数据项模型 (映射 full.proto ConversationListResponse.Data)
    /// </summary>
    public class ConversationItem : INotifyPropertyChanged
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

        private string _chatId = "";
        public string ChatId
        {
            get { return _chatId; }
            set { if (_chatId != value) { _chatId = value; OnPropertyChanged("ChatId"); OnPropertyChanged("DisplayTitle"); OnPropertyChanged("AvatarLetter"); } }
        }

        private int _chatType = 1;
        public int ChatType
        {
            get { return _chatType; }
            set { if (_chatType != value) { _chatType = value; OnPropertyChanged("ChatType"); } }
        }

        private string _remark = "";
        public string Remark
        {
            get { return _remark; }
            set { if (_remark != value) { _remark = value; OnPropertyChanged("Remark"); OnPropertyChanged("DisplayTitle"); OnPropertyChanged("AvatarLetter"); } }
        }

        private string _name = "";
        public string Name
        {
            get { return _name; }
            set { if (_name != value) { _name = value; OnPropertyChanged("Name"); OnPropertyChanged("DisplayTitle"); OnPropertyChanged("AvatarLetter"); } }
        }

        private string _chatContent = "";
        public string ChatContent
        {
            get { return _chatContent; }
            set { if (_chatContent != value) { _chatContent = value; OnPropertyChanged("ChatContent"); } }
        }

        private long _timestampMs = 0;
        public long TimestampMs
        {
            get { return _timestampMs; }
            set { if (_timestampMs != value) { _timestampMs = value; OnPropertyChanged("TimestampMs"); OnPropertyChanged("FormattedTime"); } }
        }

        private int _unreadCount = 0;
        public int UnreadCount
        {
            get { return _unreadCount; }
            set { if (_unreadCount != value) { _unreadCount = value; OnPropertyChanged("UnreadCount"); OnPropertyChanged("FormattedUnreadCount"); OnPropertyChanged("UnreadBadgeVisibility"); } }
        }

        private bool _isAt = false;
        public bool IsAt
        {
            get { return _isAt; }
            set { if (_isAt != value) { _isAt = value; OnPropertyChanged("IsAt"); } }
        }

        public long AvatarId { get; set; }

        private string _avatarUrl = "";
        public string AvatarUrl
        {
            get { return _avatarUrl; }
            set
            {
                if (_avatarUrl != value)
                {
                    _avatarUrl = value;
                    OnPropertyChanged("AvatarUrl");
                }
            }
        }

        public bool DoNotDisturb { get; set; }
        public long SendTimestamp { get; set; }
        public bool IsVip { get; set; }
        public int CertificationLevel { get; set; } // 1-官方，2-地区

        public string FormattedUnreadCount
        {
            get
            {
                if (UnreadCount <= 0) return "";
                if (UnreadCount > 99) return "99+";
                return UnreadCount.ToString();
            }
        }

        public Visibility UnreadBadgeVisibility
        {
            get { return UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed; }
        }

        private BitmapImage _avatarBitmap;
        /// <summary>
        /// 异步加载的头像位图 (支持 Referer / 七牛云 96x96 / 本地缓存)
        /// </summary>
        public BitmapImage AvatarBitmap
        {
            get { return _avatarBitmap; }
            set
            {
                if (_avatarBitmap != value)
                {
                    _avatarBitmap = value;
                    OnPropertyChanged("AvatarBitmap");
                    OnPropertyChanged("HasAvatarBitmap");
                    OnPropertyChanged("AvatarLetterVisibility");
                }
            }
        }

        public bool HasAvatarBitmap
        {
            get { return _avatarBitmap != null; }
        }

        public Visibility AvatarLetterVisibility
        {
            get { return _avatarBitmap != null ? Visibility.Collapsed : Visibility.Visible; }
        }

        /// <summary>
        /// 显示标题（优先使用备注，否则使用名称或 ID）
        /// </summary>
        public string DisplayTitle
        {
            get
            {
                if (!string.IsNullOrEmpty(Remark)) return Remark;
                if (!string.IsNullOrEmpty(Name)) return Name;
                return !string.IsNullOrEmpty(ChatId) ? ChatId : "云湖会话";
            }
        }

        /// <summary>
        /// 头像首字母（用于 Metro 纯直角占位头像）
        /// </summary>
        public string AvatarLetter
        {
            get
            {
                string title = DisplayTitle;
                if (!string.IsNullOrEmpty(title))
                {
                    return title.Substring(0, 1).ToUpper();
                }
                return "云";
            }
        }

        /// <summary>
        /// 格式化时间显示
        /// </summary>
        public string FormattedTime
        {
            get
            {
                long ts = TimestampMs > 0 ? TimestampMs : (SendTimestamp > 0 ? SendTimestamp * 1000 : 0);
                if (ts <= 0) return "";

                try
                {
                    DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    DateTime dt = epoch.AddMilliseconds(ts).ToLocalTime();
                    DateTime now = DateTime.Now;

                    if (dt.Date == now.Date)
                    {
                        return dt.ToString("HH:mm");
                    }
                    if (dt.Date == now.Date.AddDays(-1))
                    {
                        return "昨天";
                    }
                    if (dt.Year == now.Year)
                    {
                        return dt.ToString("MM-dd");
                    }
                    return dt.ToString("yyyy-MM-dd");
                }
                catch
                {
                    return "";
                }
            }
        }

        /// <summary>
        /// 原地复制更新数据（DiffUtil 刷新核心）
        /// </summary>
        public void UpdateFrom(ConversationItem other)
        {
            if (other == null) return;
            Name = other.Name;
            Remark = other.Remark;
            ChatContent = other.ChatContent;
            TimestampMs = other.TimestampMs;
            SendTimestamp = other.SendTimestamp;
            UnreadCount = other.UnreadCount;
            IsAt = other.IsAt;
            IsVip = other.IsVip;
            CertificationLevel = other.CertificationLevel;
            DoNotDisturb = other.DoNotDisturb;

            if (AvatarUrl != other.AvatarUrl)
            {
                AvatarUrl = other.AvatarUrl;
                AvatarBitmap = null;
            }

            NotifyAllChanged();
        }

        public void NotifyAllChanged()
        {
            OnPropertyChanged("DisplayTitle");
            OnPropertyChanged("AvatarLetter");
            OnPropertyChanged("FormattedTime");
            OnPropertyChanged("ChatContent");
            OnPropertyChanged("UnreadCount");
            OnPropertyChanged("FormattedUnreadCount");
            OnPropertyChanged("UnreadBadgeVisibility");
            OnPropertyChanged("AvatarBitmap");
            OnPropertyChanged("AvatarLetterVisibility");
        }
    }

    /// <summary>
    /// 对话列表 Protobuf 响应结果 (v1/conversation/list)
    /// </summary>
    public class ConversationListResult : ApiResult
    {
        public List<ConversationItem> Conversations { get; set; }
        public int Total { get; set; }
        public string Md5 { get; set; }

        public ConversationListResult()
        {
            Conversations = new List<ConversationItem>();
            Code = 1; // 默认成功
        }
    }
}
