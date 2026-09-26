using System.Collections.Generic;
using 云湖WP.Api.Common;
using 云湖WP.Api.Community;

namespace 云湖WP.Api.Community.MyActivity
{
    /// <summary>
    /// 我的动态列表 API 响应结果 (POST /v1/community/posts/my-post-list)
    /// </summary>
    public class MyPostListResult : ApiResult
    {
        public List<CommunityPostItem> Posts { get; set; }
        public int Total { get; set; }

        public MyPostListResult()
        {
            Posts = new List<CommunityPostItem>();
        }
    }

    /// <summary>
    /// 我的板块列表 API 响应结果 (POST /v1/community/ba/list-by-create)
    /// </summary>
    public class MyBoardListResult : ApiResult
    {
        public List<MyBoardItem> Boards { get; set; }

        public MyBoardListResult()
        {
            Boards = new List<MyBoardItem>();
        }
    }

    /// <summary>
    /// 我的板块条目（简化）
    /// </summary>
    public class MyBoardItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Avatar { get; set; }
    }

    /// <summary>
    /// 我的关注板块列表 API 响应结果 (POST /v1/community/ba/following-ba-list, typ=1)
    /// </summary>
    public class MyFollowingBoardListResult : ApiResult
    {
        public List<FollowingBoardItem> Boards { get; set; }
        public int Total { get; set; }

        public MyFollowingBoardListResult()
        {
            Boards = new List<FollowingBoardItem>();
        }
    }

    /// <summary>
    /// 关注的板块条目
    /// </summary>
    public class FollowingBoardItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Avatar { get; set; }
        public int MemberNum { get; set; }
        public int PostNum { get; set; }
        public string CreateTimeText { get; set; }
    }

    /// <summary>
    /// 我的收藏文章列表 API 响应结果 (POST /v1/community/posts/post-list, isCollected filter)
    /// 实际使用 post-list 接口，通过 my-post-list 配合收藏状态展示；
    /// 这里复用 MyPostListResult。
    /// </summary>
    public class MyCollectListResult : ApiResult
    {
        public List<CommunityPostItem> Posts { get; set; }
        public int Total { get; set; }

        public MyCollectListResult()
        {
            Posts = new List<CommunityPostItem>();
        }
    }
}
