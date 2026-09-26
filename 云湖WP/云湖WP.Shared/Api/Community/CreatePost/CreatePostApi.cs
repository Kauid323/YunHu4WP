using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Community.CreatePost
{
    /// <summary>
    /// 发动态 API 接口 (/v1/community/posts/create)
    /// </summary>
    public static class CreatePostApi
    {
        /// <summary>
        /// 创建/发布动态文章 (POST /v1/community/posts/create)
        /// </summary>
        /// <param name="token">登录凭证</param>
        /// <param name="baId">板块分区 ID</param>
        /// <param name="title">文章标题</param>
        /// <param name="content">文章内容</param>
        /// <param name="contentType">1-文本, 2-Markdown</param>
        /// <param name="groupId">引用群聊 ID (可选)</param>
        /// <param name="draftId">草稿 ID (若不为0则发布后云湖会自动删除该草稿)</param>
        public static async Task<CreatePostResult> CreatePostAsync(
            string token,
            int baId,
            string title,
            string content,
            int contentType = 1,
            string groupId = "",
            int draftId = 0)
        {
            var result = new CreatePostResult();
            if (string.IsNullOrEmpty(token))
            {
                result.Code = -1;
                result.Msg = "Token 为空";
                return result;
            }

            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("baId", JsonValue.CreateNumberValue(baId));
                reqObj.SetNamedValue("title", JsonValue.CreateStringValue(title ?? ""));
                reqObj.SetNamedValue("content", JsonValue.CreateStringValue(content ?? ""));
                reqObj.SetNamedValue("contentType", JsonValue.CreateNumberValue(contentType));
                reqObj.SetNamedValue("groupId", JsonValue.CreateStringValue(groupId ?? ""));
                if (draftId > 0)
                {
                    reqObj.SetNamedValue("draftId", JsonValue.CreateNumberValue(draftId));
                }

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/posts/create", reqObj.Stringify(), token);
                if (string.IsNullOrEmpty(jsonStr))
                {
                    result.Code = -1;
                    result.Msg = "响应为空";
                    return result;
                }

                JsonObject root;
                if (!JsonObject.TryParse(jsonStr, out root))
                {
                    result.Code = -1;
                    result.Msg = "JSON 解析失败";
                    return result;
                }

                result.Code = SafeGetInt(root, "code", -1);
                result.Msg = SafeGetString(root, "msg", "");

                if (result.IsSuccess && root.ContainsKey("data"))
                {
                    var dataVal = root.GetNamedValue("data");
                    if (dataVal.ValueType == JsonValueType.Object)
                    {
                        var data = dataVal.GetObject();
                        // 兼容 audioUrl / id / postId 字段
                        int pid = SafeGetInt(data, "audioUrl", 0);
                        if (pid == 0) pid = SafeGetInt(data, "id", 0);
                        if (pid == 0) pid = SafeGetInt(data, "postId", 0);
                        result.PostId = pid;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "发布动态异常: " + ex.Message;
            }

            return result;
        }

        private static string SafeGetString(JsonObject obj, string key, string defaultVal = "")
        {
            if (obj != null && obj.ContainsKey(key))
            {
                var val = obj.GetNamedValue(key);
                if (val.ValueType == JsonValueType.String) return val.GetString();
                if (val.ValueType == JsonValueType.Number) return val.GetNumber().ToString();
                if (val.ValueType == JsonValueType.Boolean) return val.GetBoolean().ToString();
            }
            return defaultVal;
        }

        private static int SafeGetInt(JsonObject obj, string key, int defaultVal = 0)
        {
            if (obj != null && obj.ContainsKey(key))
            {
                var val = obj.GetNamedValue(key);
                if (val.ValueType == JsonValueType.Number) return (int)val.GetNumber();
                if (val.ValueType == JsonValueType.String)
                {
                    int num;
                    if (int.TryParse(val.GetString(), out num)) return num;
                }
            }
            return defaultVal;
        }
    }
}
