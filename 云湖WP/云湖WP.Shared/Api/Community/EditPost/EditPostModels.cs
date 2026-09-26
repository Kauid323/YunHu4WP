using 云湖WP.Api.Common;

namespace 云湖WP.Api.Community.EditPost
{
    /// <summary>
    /// 编辑动态 API 响应结果 (POST /v1/community/posts/edit)
    /// </summary>
    public class EditPostResult : ApiResult
    {
        public long PostId { get; set; }
    }

    /// <summary>
    /// 删除动态 API 响应结果 (POST /v1/community/posts/delete)
    /// </summary>
    public class DeletePostResult : ApiResult { }

    /// <summary>
    /// 置顶/取消置顶 API 响应结果 (POST /v1/community/posts/edit-sticky)
    /// </summary>
    public class EditStickyResult : ApiResult { }

    /// <summary>
    /// 编辑动态页面导航参数
    /// </summary>
    public class EditPostNavArgs
    {
        /// <summary>文章 ID，大于 0 表示编辑模式</summary>
        public long PostId { get; set; }
        public int BaId { get; set; }
        public string BoardName { get; set; }
        public string InitialTitle { get; set; }
        public string InitialContent { get; set; }
        public int InitialContentType { get; set; }
    }
}
