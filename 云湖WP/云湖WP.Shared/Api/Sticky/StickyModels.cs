using System;
using System.Collections.Generic;
using System.ComponentModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Sticky
{
    /// <summary>
    /// 置顶会话项模型
    /// </summary>
    public class StickyItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string ChatId { get; set; }
        public int ChatType { get; set; } // 1-用户，2-群聊，3-机器人
        public string ChatName { get; set; }
        public string AvatarUrl { get; set; }
        public long Sort { get; set; }
        public long CreateTime { get; set; }
        public int DelFlag { get; set; }
        public string UserId { get; set; }
        public int CertificationLevel { get; set; }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(ChatName)) return ChatName;
                if (!string.IsNullOrEmpty(ChatId)) return ChatId;
                return "置顶会话";
            }
        }

        public string AvatarLetter
        {
            get
            {
                string name = DisplayName;
                if (!string.IsNullOrEmpty(name))
                {
                    return name.Substring(0, 1).ToUpper();
                }
                return "顶";
            }
        }

        public string TypeTag
        {
            get
            {
                switch (ChatType)
                {
                    case 2: return "[群聊]";
                    case 3: return "[机器人]";
                    default: return "[私聊]";
                }
            }
        }

        private BitmapImage _avatarBitmap = null;
        public BitmapImage AvatarBitmap
        {
            get { return _avatarBitmap; }
            set
            {
                if (_avatarBitmap != value)
                {
                    _avatarBitmap = value;
                    OnPropertyChanged("AvatarBitmap");
                    OnPropertyChanged("AvatarLetterVisibility");
                }
            }
        }

        public Visibility AvatarLetterVisibility
        {
            get { return _avatarBitmap != null ? Visibility.Collapsed : Visibility.Visible; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    /// <summary>
    /// 置顶列表响应结果 (/v1/sticky/list)
    /// </summary>
    public class StickyListResult : ApiResult
    {
        public List<StickyItem> StickyList { get; set; }
        public List<StickyItem> Items { get { return StickyList; } set { StickyList = value; } }
        public bool Success { get { return IsSuccess; } }

        public StickyListResult()
        {
            StickyList = new List<StickyItem>();
        }
    }
}
