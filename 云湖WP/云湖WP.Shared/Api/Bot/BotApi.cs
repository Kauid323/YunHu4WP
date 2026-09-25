using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Bot
{
    /// <summary>
    /// 机器人相关 API (/v1/bot 与 /v1/user/recommend)
    /// </summary>
    public static class BotApi
    {
        /// <summary>
        /// 获取推荐机器人列表 (POST /v1/user/recommend)
        /// </summary>
        public static async Task<BotRecommendListResult> GetRecommendBotsAsync(string token)
        {
            var result = new BotRecommendListResult();
            try
            {
                string jsonStr = await HttpHelper.PostJsonAsync("/v1/user/recommend", "{}", token);
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                    if (root.ContainsKey("data") && root.GetNamedValue("data").ValueType == JsonValueType.Object)
                    {
                        var dataObj = root.GetNamedObject("data");
                        if (dataObj.ContainsKey("botList") && dataObj.GetNamedValue("botList").ValueType == JsonValueType.Array)
                        {
                            var arr = dataObj.GetNamedArray("botList");
                            for (uint i = 0; i < arr.Count; i++)
                            {
                                var itemObj = arr.GetObjectAt(i);
                                var bot = new BotItem();
                                if (itemObj.ContainsKey("chatId")) bot.ChatId = itemObj.GetNamedString("chatId");
                                if (itemObj.ContainsKey("chatType"))
                                {
                                    var typeVal = itemObj.GetNamedValue("chatType");
                                    if (typeVal.ValueType == JsonValueType.Number)
                                    {
                                        bot.ChatType = (int)typeVal.GetNumber();
                                    }
                                    else if (typeVal.ValueType == JsonValueType.String)
                                    {
                                        int t = 0;
                                        int.TryParse(typeVal.GetString(), out t);
                                        bot.ChatType = t;
                                    }
                                }
                                if (itemObj.ContainsKey("headcount"))
                                {
                                    var hcVal = itemObj.GetNamedValue("headcount");
                                    if (hcVal.ValueType == JsonValueType.String)
                                    {
                                        bot.Headcount = hcVal.GetString();
                                    }
                                    else if (hcVal.ValueType == JsonValueType.Number)
                                    {
                                        bot.Headcount = hcVal.GetNumber().ToString();
                                    }
                                }
                                if (itemObj.ContainsKey("nickname")) bot.Nickname = itemObj.GetNamedString("nickname");
                                if (itemObj.ContainsKey("introduction")) bot.Introduction = itemObj.GetNamedString("introduction");
                                if (itemObj.ContainsKey("avatarUrl")) bot.AvatarUrl = itemObj.GetNamedString("avatarUrl");
                                if (itemObj.ContainsKey("isAdd")) bot.IsAdd = (int)itemObj.GetNamedNumber("isAdd");
                                if (itemObj.ContainsKey("isApply")) bot.IsApply = (int)itemObj.GetNamedNumber("isApply");
                                if (itemObj.ContainsKey("alwaysAgree")) bot.AlwaysAgree = (int)itemObj.GetNamedNumber("alwaysAgree");
                                result.BotList.Add(bot);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取推荐机器人失败: " + ex.Message;
            }
            return result;
        }
    }
}
