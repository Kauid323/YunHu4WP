using System;
using System.Collections.Generic;
using System.ComponentModel;
using Windows.UI.Xaml.Media.Imaging;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.WebApi.User
{
    /// <summary>
    /// Web API 用户主页信息 (/v1/user/homepage)
    /// </summary>
    public class UserWebHomepage : INotifyPropertyChanged
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

        private long _registerTime;
        public long RegisterTime
        {
            get { return _registerTime; }
            set
            {
                if (_registerTime != value)
                {
                    _registerTime = value;
                    OnPropertyChanged("RegisterTime");
                    OnPropertyChanged("DisplayRegisterTime");
                }
            }
        }

        private string _registerTimeText;
        public string RegisterTimeText
        {
            get { return _registerTimeText; }
            set
            {
                if (_registerTimeText != value)
                {
                    _registerTimeText = value;
                    OnPropertyChanged("RegisterTimeText");
                    OnPropertyChanged("DisplayRegisterTime");
                }
            }
        }

        private int _onLineDay;
        public int OnLineDay
        {
            get { return _onLineDay; }
            set
            {
                if (_onLineDay != value)
                {
                    _onLineDay = value;
                    OnPropertyChanged("OnLineDay");
                }
            }
        }

        private int _continuousOnLineDay;
        public int ContinuousOnLineDay
        {
            get { return _continuousOnLineDay; }
            set
            {
                if (_continuousOnLineDay != value)
                {
                    _continuousOnLineDay = value;
                    OnPropertyChanged("ContinuousOnLineDay");
                }
            }
        }

        private int _isVip;
        public int IsVip
        {
            get { return _isVip; }
            set
            {
                if (_isVip != value)
                {
                    _isVip = value;
                    OnPropertyChanged("IsVip");
                    OnPropertyChanged("IsVipUser");
                }
            }
        }

        private List<UserWebMedal> _medals = new List<UserWebMedal>();
        public List<UserWebMedal> Medals
        {
            get { return _medals; }
            set
            {
                if (_medals != value)
                {
                    _medals = value;
                    OnPropertyChanged("Medals");
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

        public bool IsVipUser
        {
            get { return IsVip == 1; }
        }

        public string DisplayRegisterTime
        {
            get
            {
                if (!string.IsNullOrEmpty(RegisterTimeText)) return RegisterTimeText;
                if (RegisterTime > 0)
                {
                    try
                    {
                        var dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                            .AddSeconds(RegisterTime)
                            .ToLocalTime();
                        return dt.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                    catch { }
                }
                return "-";
            }
        }
    }

    /// <summary>
    /// Web API 用户勋章信息
    /// </summary>
    public class UserWebMedal
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Desc { get; set; }
        public string ImageUrl { get; set; }
        public int Sort { get; set; }
    }

    /// <summary>
    /// Web API 用户主页接口响应
    /// </summary>
    public class UserWebHomepageResult : ApiResult
    {
        public UserWebHomepage User { get; set; }
    }

    /// <summary>
    /// Web API 用户自身信息响应 (/v1/user/info)
    /// </summary>
    public class UserWebSelfInfoResult : ApiResult
    {
        public string UserId { get; set; }
        public string Nickname { get; set; }
        public string Phone { get; set; }
        public string AvatarId { get; set; }
        public string AvatarUrl { get; set; }
        public double GoldCoinAmount { get; set; }
    }
}
