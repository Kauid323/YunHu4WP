using System;
using System.Collections.Generic;
using System.ComponentModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Friend
{
    /// <summary>
    /// 通讯录联系人条目 (支持好友、群聊、机器人三种类型)
    /// </summary>
    public class FriendContactItem : INotifyPropertyChanged
    {
        public string ChatId { get; set; }
        public string Name { get; set; }
        public string AvatarUrl { get; set; }
        public int PermissionLevel { get; set; } // 0-普通成员, 2-管理员, 100-群主
        public bool NoDisturb { get; set; }
        public int ChatType { get; set; } // 1-用户/好友, 2-群聊, 3-机器人

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Name)) return Name;
                if (!string.IsNullOrEmpty(ChatId)) return ChatId;
                switch (ChatType)
                {
                    case 2: return "群聊";
                    case 3: return "机器人";
                    default: return "用户";
                }
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
                switch (ChatType)
                {
                    case 2: return "群";
                    case 3: return "机";
                    default: return "友";
                }
            }
        }

        public string RoleTag
        {
            get
            {
                if (ChatType == 2)
                {
                    if (PermissionLevel == 100) return " [群主]";
                    if (PermissionLevel == 2) return " [管理员]";
                }
                return "";
            }
        }

        public string Subtitle
        {
            get
            {
                if (ChatType == 2)
                {
                    string role = PermissionLevel == 100 ? "群主" : (PermissionLevel == 2 ? "管理员" : "群成员");
                    return string.Format("群聊 (我的身份: {0})", role);
                }
                return "ID: " + (ChatId ?? "");
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
    /// 通讯录响应结果 (/v1/friend/address-book-list)
    /// </summary>
    public class AddressBookResult : ApiResult
    {
        public List<FriendContactItem> Friends { get; set; }
        public List<FriendContactItem> Groups { get; set; }
        public List<FriendContactItem> Bots { get; set; }

        public AddressBookResult()
        {
            Friends = new List<FriendContactItem>();
            Groups = new List<FriendContactItem>();
            Bots = new List<FriendContactItem>();
        }
    }

    /// <summary>
    /// 添加好友/群聊/机器人请求
    /// </summary>
    public class AddFriendRequest
    {
        public string ChatId { get; set; }
        public int ChatType { get; set; } // 1-用户，2-群聊，3-机器人
        public string Remark { get; set; }
    }
}
