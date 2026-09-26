using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;
using 云湖WP.Utils;

namespace 云湖WP.Api.Sticky
{
    /// <summary>
    /// 置顶会话相关 API (/v1/sticky)
    /// </summary>
    public static class StickyApi
    {
        /// <summary>
        /// 获取所有置顶会话列表 (POST /v1/sticky/list)
        /// </summary>
        public static async Task<StickyListResult> GetStickyListAsync(string token)
        {
            var result = new StickyListResult();
            if (string.IsNullOrEmpty(token))
            {
                result.Code = -1;
                result.Msg = "Token 为空";
                return result;
            }

            try
            {
                string jsonStr = await HttpHelper.PostJsonAsync("/v1/sticky/list", "{}", token);
                AppLogger.Log("StickyApi", "GetStickyListAsync response: " + jsonStr);

                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                    if (root.ContainsKey("message")) result.Msg = root.GetNamedString("message");

                    if (root.ContainsKey("data") && root.GetNamedValue("data").ValueType == JsonValueType.Object)
                    {
                        var dataObj = root.GetNamedObject("data");
                        if (dataObj.ContainsKey("sticky") && dataObj.GetNamedValue("sticky").ValueType == JsonValueType.Array)
                        {
                            var arr = dataObj.GetNamedArray("sticky");
                            foreach (var itemVal in arr)
                            {
                                if (itemVal.ValueType != JsonValueType.Object) continue;
                                var obj = itemVal.GetObject();

                                var item = new StickyItem();
                                if (obj.ContainsKey("id")) item.Id = (int)obj.GetNamedNumber("id");
                                if (obj.ContainsKey("chatId")) item.ChatId = obj.GetNamedString("chatId");
                                if (obj.ContainsKey("chatType")) item.ChatType = (int)obj.GetNamedNumber("chatType");
                                if (obj.ContainsKey("chatName")) item.ChatName = obj.GetNamedString("chatName");
                                if (obj.ContainsKey("avatarUrl")) item.AvatarUrl = obj.GetNamedString("avatarUrl");
                                if (obj.ContainsKey("sort")) item.Sort = (long)obj.GetNamedNumber("sort");
                                if (obj.ContainsKey("createTime")) item.CreateTime = (long)obj.GetNamedNumber("createTime");
                                if (obj.ContainsKey("delFlag")) item.DelFlag = (int)obj.GetNamedNumber("delFlag");
                                if (obj.ContainsKey("userId")) item.UserId = obj.GetNamedString("userId");
                                if (obj.ContainsKey("certificationLevel")) item.CertificationLevel = (int)obj.GetNamedNumber("certificationLevel");

                                result.StickyList.Add(item);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取置顶列表异常: " + ex.Message;
                AppLogger.Log("StickyApi", "GetStickyListAsync error: " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// 添加置顶 (POST /v1/sticky/add)
        /// </summary>
        public static async Task<ApiResult> AddStickyAsync(string token, string chatId, int chatType)
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("chatId", JsonValue.CreateStringValue(chatId));
                reqObj.SetNamedValue("chatType", JsonValue.CreateNumberValue(chatType));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/sticky/add", reqObj.Stringify(), token);
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "添加置顶失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 取消置顶 (POST /v1/sticky/delete)
        /// </summary>
        public static async Task<ApiResult> DeleteStickyAsync(string token, string chatId, int chatType)
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("chatId", JsonValue.CreateStringValue(chatId));
                reqObj.SetNamedValue("chatType", JsonValue.CreateNumberValue(chatType));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/sticky/delete", reqObj.Stringify(), token);
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "取消置顶失败: " + ex.Message;
            }
            return result;
        }
    }
}
