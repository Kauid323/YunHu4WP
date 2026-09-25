using System;
using System.Collections.Generic;
using System.ComponentModel;
using Windows.UI.Xaml.Media.Imaging;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Community.PostDetail
{
    /// <summary>
    /// 单条文章详情 API 响应结果
    /// </summary>
    public class CommunityPostDetailResult : ApiResult
    {
        public CommunityPostItem Post { get; set; }
    }

    /// <summary>
    /// 文章评论数据模型
    /// </summary>
    public class CommunityCommentItem : INotifyPropertyChanged
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

        public long Id { get; set; }
        public long PostId { get; set; }
        public long ParentId { get; set; }
        public string SenderId { get; set; }
        public string SenderNickname { get; set; }
        public string SenderAvatar { get; set; }
        public string Content { get; set; }
        public string CreateTimeText { get; set; }
        public long CreateTime { get; set; }
        public int LikeNum { get; set; }
        public double AmountNum { get; set; }
        public bool IsLiked { get; set; }

        public string DisplayAuthor
        {
            get
            {
                if (!string.IsNullOrEmpty(SenderNickname)) return SenderNickname;
                if (!string.IsNullOrEmpty(SenderId)) return SenderId;
                return "湖友";
            }
        }

        public string DisplayAvatarLetter
        {
            get
            {
                string name = DisplayAuthor;
                if (!string.IsNullOrEmpty(name))
                {
                    return name.Substring(0, 1).ToUpper();
                }
                return "湖";
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

        public string DisplayTime
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
                        return dt.ToString("MM-dd HH:mm");
                    }
                    catch { }
                }
                return "";
            }
        }
    }

    /// <summary>
    /// 评论列表 API 响应结果
    /// </summary>
    public class CommunityCommentListResult : ApiResult
    {
        public List<CommunityCommentItem> Comments { get; set; }
        public int Total { get; set; }

        public CommunityCommentListResult()
        {
            Comments = new List<CommunityCommentItem>();
        }
    }

    /// <summary>
    /// 动态详情页面导航参数
    /// </summary>
    public class PostDetailNavigationArgs
    {
        public long PostId { get; set; }
        public CommunityPostItem InitialPost { get; set; }
        public string Token { get; set; }
    }
}
