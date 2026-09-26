using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Community.PostDraft
{
    /// <summary>
    /// 社区草稿箱 API 接口 (/v1/community/posts/*-draft)
    /// </summary>
    public static class PostDraftApi
    {
        /// <summary>
        /// 保存文章草稿 (POST /v1/community/posts/create-draft)
        /// 若 draftId 不为 0，云湖会删除旧草稿并保存新草稿，以此实现草稿编辑
        /// </summary>
        public static async Task<SaveDraftResult> SaveDraftAsync(
            string token,
            int baId,
            string title,
            string content,
            int contentType = 1,
            int draftId = 0)
        {
            var result = new SaveDraftResult();
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
                reqObj.SetNamedValue("draftId", JsonValue.CreateNumberValue(draftId));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/posts/create-draft", reqObj.Stringify(), token);
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
                        result.DraftId = SafeGetInt(data, "id", 0);
                        if (result.DraftId == 0) result.DraftId = SafeGetInt(data, "draftId", 0);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "保存草稿异常: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 获取文章草稿信息 (POST /v1/community/posts/get-draft)
        /// </summary>
        public static async Task<GetDraftResult> GetDraftAsync(string token, int draftId, int baId = 0)
        {
            var result = new GetDraftResult();
            if (string.IsNullOrEmpty(token))
            {
                result.Code = -1;
                result.Msg = "Token 为空";
                return result;
            }

            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("draftId", JsonValue.CreateNumberValue(draftId));
                reqObj.SetNamedValue("baId", JsonValue.CreateNumberValue(baId));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/posts/get-draft", reqObj.Stringify(), token);
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
                        JsonObject postObj = null;
                        if (data.ContainsKey("posts") && data.GetNamedValue("posts").ValueType == JsonValueType.Object)
                        {
                            postObj = data.GetNamedObject("posts");
                        }
                        else if (data.ContainsKey("post") && data.GetNamedValue("post").ValueType == JsonValueType.Object)
                        {
                            postObj = data.GetNamedObject("post");
                        }
                        else
                        {
                            postObj = data;
                        }

                        if (postObj != null)
                        {
                            result.DraftId = SafeGetInt(postObj, "id", draftId);
                            result.BaId = SafeGetInt(postObj, "baId", 0);
                            result.Title = SafeGetString(postObj, "title", "");
                            result.Content = SafeGetString(postObj, "content", "");
                            result.ContentType = SafeGetInt(postObj, "contentType", 1);
                            result.CreateTime = SafeGetLong(postObj, "createTime", 0);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取草稿异常: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 删除文章草稿 (POST /v1/community/posts/cancel-draft)
        /// </summary>
        public static async Task<ApiResult> DeleteDraftAsync(string token, int draftId)
        {
            var result = new ApiResult();
            if (string.IsNullOrEmpty(token))
            {
                result.Code = -1;
                result.Msg = "Token 为空";
                return result;
            }

            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("draftId", JsonValue.CreateNumberValue(draftId));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/posts/cancel-draft", reqObj.Stringify(), token);
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
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "删除草稿异常: " + ex.Message;
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

        private static long SafeGetLong(JsonObject obj, string key, long defaultVal = 0)
        {
            if (obj != null && obj.ContainsKey(key))
            {
                var val = obj.GetNamedValue(key);
                if (val.ValueType == JsonValueType.Number) return (long)val.GetNumber();
                if (val.ValueType == JsonValueType.String)
                {
                    long num;
                    if (long.TryParse(val.GetString(), out num)) return num;
                }
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
