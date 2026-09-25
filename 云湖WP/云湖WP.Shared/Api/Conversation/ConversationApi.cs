using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Conversation
{
    /// <summary>
    /// 会话与置顶消息相关 API (/v1/conversation 与 /v1/sticky)
    /// </summary>
    public static class ConversationApi
    {
        /// <summary>
        /// 获取会话列表 (/v1/conversation/list - 基于 Protobuf 二进制协议)
        /// </summary>
        public static async Task<ConversationListResult> GetConversationListAsync(string token, string md5 = "")
        {
            var result = new ConversationListResult();
            if (string.IsNullOrEmpty(token))
            {
                result.Code = -1;
                result.Msg = "Token 为空";
                return result;
            }

            try
            {
                // 优先使用 GET 获取 Protobuf 会话数据
                byte[] responseBytes = await HttpHelper.GetProtobufAsync("/v1/conversation/list", token);

                // 如果 GET 返回空，尝试 POST 请求体获取
                if (responseBytes == null || responseBytes.Length == 0)
                {
                    byte[] requestBytes = string.IsNullOrEmpty(md5) ? null : 云湖WP.Api.Protobuf.ConversationProtobufCodec.EncodeListRequest(md5);
                    responseBytes = await HttpHelper.PostProtobufAsync("/v1/conversation/list", requestBytes, token);
                }

                return 云湖WP.Api.Protobuf.ConversationProtobufCodec.DecodeListResponse(responseBytes);
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取会话列表网络异常: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 获取首页置顶会话列表 (POST /v1/sticky/list)
        /// </summary>
        public static async Task<StickyListResult> GetStickyListAsync(string token)
        {
            var result = new StickyListResult();
            try
            {
                string jsonStr = await HttpHelper.PostJsonAsync("/v1/sticky/list", "{}", token);
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                    if (root.ContainsKey("data") && root.GetNamedValue("data").ValueType == JsonValueType.Object)
                    {
                        var dataObj = root.GetNamedObject("data");
                        if (dataObj.ContainsKey("sticky") && dataObj.GetNamedValue("sticky").ValueType == JsonValueType.Array)
                        {
                            var arr = dataObj.GetNamedArray("sticky");
                            for (uint i = 0; i < arr.Count; i++)
                            {
                                var itemObj = arr.GetObjectAt(i);
                                var item = new StickyItem();
                                if (itemObj.ContainsKey("id")) item.Id = (int)itemObj.GetNamedNumber("id");
                                if (itemObj.ContainsKey("chatType")) item.ChatType = (int)itemObj.GetNamedNumber("chatType");
                                if (itemObj.ContainsKey("chatId")) item.ChatId = itemObj.GetNamedString("chatId");
                                if (itemObj.ContainsKey("chatName")) item.ChatName = itemObj.GetNamedString("chatName");
                                if (itemObj.ContainsKey("sort")) item.Sort = (long)itemObj.GetNamedNumber("sort");
                                if (itemObj.ContainsKey("avatarUrl")) item.AvatarUrl = itemObj.GetNamedString("avatarUrl");
                                if (itemObj.ContainsKey("createTime")) item.CreateTime = (long)itemObj.GetNamedNumber("createTime");
                                if (itemObj.ContainsKey("delFlag")) item.DelFlag = (int)itemObj.GetNamedNumber("delFlag");
                                if (itemObj.ContainsKey("userId")) item.UserId = itemObj.GetNamedString("userId");
                                if (itemObj.ContainsKey("certificationLevel")) item.CertificationLevel = (int)itemObj.GetNamedNumber("certificationLevel");
                                result.StickyList.Add(item);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取置顶会话失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 添加置顶会话 (POST /v1/sticky/add)
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
        /// 取消置顶会话 (POST /v1/sticky/delete)
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

        /// <summary>
        /// 置顶移至最前 (POST /v1/sticky/topping)
        /// </summary>
        public static async Task<ApiResult> TopStickyAsync(string token, int stickyId)
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("id", JsonValue.CreateNumberValue(stickyId));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/sticky/topping", reqObj.Stringify(), token);
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
                result.Msg = "置顶移动失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 将会话设为已读 (POST /v1/conversation/dismiss-notification)
        /// </summary>
        public static async Task<ApiResult> DismissNotificationAsync(string token, string chatId)
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("chatId", JsonValue.CreateStringValue(chatId));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/conversation/dismiss-notification", reqObj.Stringify(), token);
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
                result.Msg = "已读标记失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 删除会话 (POST /v1/conversation/remove)
        /// </summary>
        public static async Task<ApiResult> RemoveConversationAsync(string token, string chatId)
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("chatId", JsonValue.CreateStringValue(chatId));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/conversation/remove", reqObj.Stringify(), token);
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
                result.Msg = "删除会话失败: " + ex.Message;
            }
            return result;
        }
    }
}
