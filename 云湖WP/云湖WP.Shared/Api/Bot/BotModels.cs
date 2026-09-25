using System;
using System.Collections.Generic;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Bot
{
    /// <summary>
    /// 机器人实体模型
    /// </summary>
    public class BotItem
    {
        public string ChatId { get; set; }
        public int ChatType { get; set; }
        public string Headcount { get; set; }
        public string Nickname { get; set; }
        public string Introduction { get; set; }
        public string AvatarUrl { get; set; }
        public int IsAdd { get; set; }
        public int IsApply { get; set; }
        public int AlwaysAgree { get; set; }
    }

    /// <summary>
    /// 推荐机器人列表响应 (v1/user/recommend)
    /// </summary>
    public class BotRecommendListResult : ApiResult
    {
        public List<BotItem> BotList { get; set; }

        public BotRecommendListResult()
        {
            BotList = new List<BotItem>();
        }
    }
}
