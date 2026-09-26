using System;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Community.CreatePost
{
    /// <summary>
    /// 发布动态请求参数
    /// </summary>
    public class CreatePostRequest
    {
        public int BaId { get; set; }
        public string GroupId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public int ContentType { get; set; } // 1-文本, 2-Markdown
        public int DraftId { get; set; }
    }

    /// <summary>
    /// 发布动态响应结果 (/v1/community/posts/create)
    /// </summary>
    public class CreatePostResult : ApiResult
    {
        public int PostId { get; set; }
    }

    /// <summary>
    /// 发动态页面导航参数
    /// </summary>
    public class CreatePostNavArgs
    {
        /// <summary>大于 0 时为编辑现有动态模式，否则为新发动态</summary>
        public long PostId { get; set; }
        public int BaId { get; set; }
        public string BoardName { get; set; }
        public int DraftId { get; set; }
        public string InitialTitle { get; set; }
        public string InitialContent { get; set; }
        public int InitialContentType { get; set; } // 1-文本, 2-Markdown
    }
}
