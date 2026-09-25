using System;
using System.Collections.Generic;
using System.ComponentModel;
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

        public string ChatId { get; set; }
        public int ChatType { get; set; } // 1-用户，2-群聊，3-机器人
        public string Remark { get; set; }
        public string Name { get; set; }
        public string ChatContent { get; set; }
        public long TimestampMs { get; set; }
        public int UnreadCount { get; set; }
        public bool IsAt { get; set; }
        public long AvatarId { get; set; }
        public string AvatarUrl { get; set; }
        public bool DoNotDisturb { get; set; }
        public long SendTimestamp { get; set; }
        public bool IsVip { get; set; }
        public int CertificationLevel { get; set; } // 1-官方，2-地区

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
                }
            }
        }

        public bool HasAvatarBitmap
        {
            get { return _avatarBitmap != null; }
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

    /// <summary>
    /// 置顶会话条目
    /// </summary>
    public class StickyItem
    {
        public int Id { get; set; }
        public int ChatType { get; set; } // 1-用户，2-群聊，3-机器人
        public string ChatId { get; set; }
        public string ChatName { get; set; }
        public long Sort { get; set; }
        public string AvatarUrl { get; set; }
        public long CreateTime { get; set; }
        public int DelFlag { get; set; }
        public string UserId { get; set; }
        public int CertificationLevel { get; set; } // 0-非官方，1-官方，2-地区
    }

    /// <summary>
    /// 置顶会话列表结果 (v1/sticky/list)
    /// </summary>
    public class StickyListResult : ApiResult
    {
        public List<StickyItem> StickyList { get; set; }

        public StickyListResult()
        {
            StickyList = new List<StickyItem>();
        }
    }
}
