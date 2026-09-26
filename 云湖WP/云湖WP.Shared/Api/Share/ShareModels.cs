using System;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Share
{
    /// <summary>
    /// 分享链接解析详情条目
    /// </summary>
    public class ShareInfoItem
    {
        public long Id { get; set; }
        public string UserId { get; set; }
        public string ChatName { get; set; }
        public int ChatType { get; set; } // 1-用户，2-群聊，3-机器人
        public string ChatId { get; set; }
        public string Key { get; set; }
        public string CreateBy { get; set; }
        public long CreateTime { get; set; }
        public string ImageUrl { get; set; }
        public string ImageName { get; set; }
    }

    /// <summary>
    /// 分享信息响应结果 (POST /v1/share/info)
    /// </summary>
    public class ShareInfoResult : ApiResult
    {
        public ShareInfoItem Share { get; set; }
    }

    /// <summary>
    /// 创建分享响应结果 (POST /v1/share/create)
    /// </summary>
    public class CreateShareResult : ApiResult
    {
        public string Key { get; set; }
        public string ShareUrl { get; set; }
        public string ImageKey { get; set; }
        public long Ts { get; set; }
    }
}
