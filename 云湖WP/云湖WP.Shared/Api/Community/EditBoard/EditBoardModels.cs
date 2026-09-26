using 云湖WP.Api.Common;

namespace 云湖WP.Api.Community.EditBoard
{
    /// <summary>
    /// 编辑板块 API 响应结果 (POST /v1/community/ba/edit)
    /// </summary>
    public class EditBoardResult : ApiResult { }

    /// <summary>
    /// 编辑板块页面导航参数
    /// </summary>
    public class EditBoardNavArgs
    {
        public int BaId { get; set; }
        public string CurrentName { get; set; }
        public string CurrentAvatar { get; set; }
    }
}
