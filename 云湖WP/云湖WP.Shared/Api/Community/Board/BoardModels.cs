using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Windows.UI.Xaml.Media.Imaging;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Community.Board
{
    /// <summary>
    /// 社区板块/分区数据模型 (/v1/community/ba/*)
    /// </summary>
    public class BoardInfoItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private int _id;
        public int Id
        {
            get { return _id; }
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged("Id");
                }
            }
        }

        private string _name;
        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged("Name");
                    OnPropertyChanged("DisplayName");
                    OnPropertyChanged("DisplayAvatarLetter");
                }
            }
        }

        private string _avatar;
        public string Avatar
        {
            get { return _avatar; }
            set
            {
                if (_avatar != value)
                {
                    _avatar = value;
                    OnPropertyChanged("Avatar");
                }
            }
        }

        private long _delTime;
        public long DelTime
        {
            get { return _delTime; }
            set
            {
                if (_delTime != value)
                {
                    _delTime = value;
                    OnPropertyChanged("DelTime");
                }
            }
        }

        private long _createTime;
        public long CreateTime
        {
            get { return _createTime; }
            set
            {
                if (_createTime != value)
                {
                    _createTime = value;
                    OnPropertyChanged("CreateTime");
                    OnPropertyChanged("DisplayCreateTime");
                }
            }
        }

        private long _lastActive;
        public long LastActive
        {
            get { return _lastActive; }
            set
            {
                if (_lastActive != value)
                {
                    _lastActive = value;
                    OnPropertyChanged("LastActive");
                    OnPropertyChanged("DisplayLastActive");
                }
            }
        }

        private int _memberNum;
        public int MemberNum
        {
            get { return _memberNum; }
            set
            {
                if (_memberNum != value)
                {
                    _memberNum = value;
                    OnPropertyChanged("MemberNum");
                    OnPropertyChanged("StatsSummary");
                }
            }
        }

        private int _postNum;
        public int PostNum
        {
            get { return _postNum; }
            set
            {
                if (_postNum != value)
                {
                    _postNum = value;
                    OnPropertyChanged("PostNum");
                    OnPropertyChanged("StatsSummary");
                }
            }
        }

        private int _groupNum;
        public int GroupNum
        {
            get { return _groupNum; }
            set
            {
                if (_groupNum != value)
                {
                    _groupNum = value;
                    OnPropertyChanged("GroupNum");
                    OnPropertyChanged("StatsSummary");
                }
            }
        }

        private string _createTimeText;
        public string CreateTimeText
        {
            get { return _createTimeText; }
            set
            {
                if (_createTimeText != value)
                {
                    _createTimeText = value;
                    OnPropertyChanged("CreateTimeText");
                    OnPropertyChanged("DisplayCreateTime");
                }
            }
        }

        private bool _isFollowed;
        public bool IsFollowed
        {
            get { return _isFollowed; }
            set
            {
                if (_isFollowed != value)
                {
                    _isFollowed = value;
                    OnPropertyChanged("IsFollowed");
                    OnPropertyChanged("FollowButtonText");
                }
            }
        }

        private string _createBy;
        public string CreateBy
        {
            get { return _createBy; }
            set
            {
                if (_createBy != value)
                {
                    _createBy = value;
                    OnPropertyChanged("CreateBy");
                }
            }
        }

        private BoardFollowerItem _owner;
        public BoardFollowerItem Owner
        {
            get { return _owner; }
            set
            {
                if (_owner != value)
                {
                    _owner = value;
                    OnPropertyChanged("Owner");
                }
            }
        }

        private ObservableCollection<BoardFollowerItem> _admins = new ObservableCollection<BoardFollowerItem>();
        public ObservableCollection<BoardFollowerItem> Admins
        {
            get { return _admins; }
            set
            {
                if (_admins != value)
                {
                    _admins = value;
                    OnPropertyChanged("Admins");
                }
            }
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Name)) return Name;
                if (Id > 0) return "板块 " + Id;
                return "未知板块";
            }
        }

        public string DisplayAvatarLetter
        {
            get
            {
                string n = DisplayName;
                if (!string.IsNullOrEmpty(n))
                {
                    return n.Substring(0, 1).ToUpper();
                }
                return "板";
            }
        }

        private BitmapImage _avatarBitmap;
        public BitmapImage AvatarBitmap
        {
            get { return _avatarBitmap; }
            set
            {
                if (_avatarBitmap != value)
                {
                    _avatarBitmap = value;
                    OnPropertyChanged("AvatarBitmap");
                }
            }
        }

        public string DisplayCreateTime
        {
            get
            {
                if (!string.IsNullOrEmpty(CreateTimeText)) return CreateTimeText;
                if (CreateTime > 0)
                {
                    try
                    {
                        var dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                            .AddSeconds(CreateTime)
                            .ToLocalTime();
                        return dt.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                    catch { }
                }
                return "-";
            }
        }

        public string DisplayLastActive
        {
            get
            {
                if (LastActive > 0)
                {
                    try
                    {
                        var dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                            .AddSeconds(LastActive)
                            .ToLocalTime();
                        return dt.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                    catch { }
                }
                return "-";
            }
        }

        public string FollowButtonText
        {
            get { return IsFollowed ? "已关注" : "+ 关注板块"; }
        }

        public string StatsSummary
        {
            get
            {
                return string.Format("{0} 成员 · {1} 动态 · {2} 关联群", MemberNum, PostNum, GroupNum);
            }
        }
    }

    /// <summary>
    /// 板块关注者/成员/管理员/板块主数据模型
    /// </summary>
    public class BoardFollowerItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private int _id;
        public int Id
        {
            get { return _id; }
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged("Id");
                }
            }
        }

        private int _baId;
        public int BaId
        {
            get { return _baId; }
            set
            {
                if (_baId != value)
                {
                    _baId = value;
                    OnPropertyChanged("BaId");
                }
            }
        }

        private string _userId;
        public string UserId
        {
            get { return _userId; }
            set
            {
                if (_userId != value)
                {
                    _userId = value;
                    OnPropertyChanged("UserId");
                    OnPropertyChanged("DisplayName");
                    OnPropertyChanged("DisplayAvatarLetter");
                }
            }
        }

        private string _nickname;
        public string Nickname
        {
            get { return _nickname; }
            set
            {
                if (_nickname != value)
                {
                    _nickname = value;
                    OnPropertyChanged("Nickname");
                    OnPropertyChanged("DisplayName");
                    OnPropertyChanged("DisplayAvatarLetter");
                }
            }
        }

        private string _avatarUrl;
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

        private int _userLevel; // 0-普通成员, 1-板块主/创建者, 2-分区管理员
        public int UserLevel
        {
            get { return _userLevel; }
            set
            {
                if (_userLevel != value)
                {
                    _userLevel = value;
                    OnPropertyChanged("UserLevel");
                    OnPropertyChanged("RoleText");
                    OnPropertyChanged("IsAdmin");
                    OnPropertyChanged("IsOwner");
                }
            }
        }

        private long _createTime;
        public long CreateTime
        {
            get { return _createTime; }
            set
            {
                if (_createTime != value)
                {
                    _createTime = value;
                    OnPropertyChanged("CreateTime");
                }
            }
        }

        private string _vipUserId;
        public string VipUserId
        {
            get { return _vipUserId; }
            set
            {
                if (_vipUserId != value)
                {
                    _vipUserId = value;
                    OnPropertyChanged("VipUserId");
                }
            }
        }

        private long _vipEndTime;
        public long VipEndTime
        {
            get { return _vipEndTime; }
            set
            {
                if (_vipEndTime != value)
                {
                    _vipEndTime = value;
                    OnPropertyChanged("VipEndTime");
                }
            }
        }

        private BitmapImage _avatarBitmap;
        public BitmapImage AvatarBitmap
        {
            get { return _avatarBitmap; }
            set
            {
                if (_avatarBitmap != value)
                {
                    _avatarBitmap = value;
                    OnPropertyChanged("AvatarBitmap");
                }
            }
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Nickname)) return Nickname;
                if (!string.IsNullOrEmpty(UserId)) return "用户 " + UserId;
                return "未知用户";
            }
        }

        public string DisplayAvatarLetter
        {
            get
            {
                string n = DisplayName;
                if (!string.IsNullOrEmpty(n))
                {
                    return n.Substring(0, 1).ToUpper();
                }
                return "用";
            }
        }

        public string RoleText
        {
            get
            {
                if (UserLevel == 1) return "板块主";
                if (UserLevel == 2) return "管理员";
                return "成员";
            }
        }

        public bool IsOwner
        {
            get { return UserLevel == 1; }
        }

        public bool IsAdmin
        {
            get { return UserLevel == 2; }
        }
    }

    /// <summary>
    /// 板块关注者/成员列表响应
    /// </summary>
    public class BoardFollowerListResult : ApiResult
    {
        public List<BoardFollowerItem> Followers { get; set; }
        public int Total { get; set; }

        public BoardFollowerListResult()
        {
            Followers = new List<BoardFollowerItem>();
        }
    }

    /// <summary>
    /// 板块信息详情接口响应
    /// </summary>
    public class BoardInfoResult : ApiResult
    {
        public BoardInfoItem Board { get; set; }
    }

    /// <summary>
    /// 板块页面导航参数
    /// </summary>
    public class BoardDetailNavArgs
    {
        public int BaId { get; set; }
        public string BoardName { get; set; }
        public string BoardAvatar { get; set; }
        public string Token { get; set; }
        public BoardInfoItem InitialBoard { get; set; }
    }
}
