using System;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Community.PostDraft
{
    /// <summary>
    /// 保存草稿请求参数
    /// </summary>
    public class SaveDraftRequest
    {
        public int BaId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public int ContentType { get; set; } // 1-文本, 2-Markdown
        public int DraftId { get; set; }
    }

    /// <summary>
    /// 保存草稿响应结果 (/v1/community/posts/create-draft)
    /// </summary>
    public class SaveDraftResult : ApiResult
    {
        public int DraftId { get; set; }
    }

    /// <summary>
    /// 获取草稿详情响应结果 (/v1/community/posts/get-draft)
    /// </summary>
    public class GetDraftResult : ApiResult
    {
        public int DraftId { get; set; }
        public int BaId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public int ContentType { get; set; }
        public long CreateTime { get; set; }
    }
}
