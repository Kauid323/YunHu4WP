using System;
using System.Collections.Generic;
using System.ComponentModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Community
{
    /// <summary>
    /// 社区动态/文章数据模型 (包含点赞、评论、收藏数与投币数)
    /// </summary>
    public class CommunityPostItem : INotifyPropertyChanged
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
        public int BaId { get; set; }
        public string SenderId { get; set; }
        public string SenderNickname { get; set; }
        public string SenderAvatar { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public int ContentType { get; set; } // 1-普通文本, 2-Markdown
        public string CreateTimeText { get; set; }
        public long CreateTime { get; set; }

        public int LikeNum { get; set; }
        public int CommentNum { get; set; }
        public int CollectNum { get; set; } // 收藏数
        public double AmountNum { get; set; } // 投币数

        public bool IsLiked { get; set; }
        public bool IsCollected { get; set; }
        public bool IsReward { get; set; }

        /// <summary>是否为草稿 (1=草稿, 0=已发布)</summary>
        public bool IsDraft { get; set; }

        public Visibility DraftBadgeVisibility
        {
            get { return IsDraft ? Visibility.Visible : Visibility.Collapsed; }
        }

        /// <summary>置顶时间戳，0=未置顶，非0=已置顶</summary>
        public int IsSticky { get; set; }

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

        public string LikeText
        {
            get { return string.Format("{0} 赞", LikeNum); }
        }

        public string CommentText
        {
            get { return string.Format("{0} 评", CommentNum); }
        }

        public string CollectText
        {
            get { return string.Format("{0} 藏", CollectNum); }
        }

        public string AmountText
        {
            get { return string.Format("{0:0.#} 币", AmountNum); }
        }
    }

    /// <summary>
    /// 社区文章列表 API 响应结果
    /// </summary>
    public class CommunityPostListResult : ApiResult
    {
        public List<CommunityPostItem> Posts { get; set; }
        public int Total { get; set; }

        public CommunityPostListResult()
        {
            Posts = new List<CommunityPostItem>();
        }
    }
}
