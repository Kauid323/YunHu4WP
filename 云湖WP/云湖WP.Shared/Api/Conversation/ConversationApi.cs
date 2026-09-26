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
