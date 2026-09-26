using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;
using 云湖WP.Utils;

namespace 云湖WP.Api.Share
{
    /// <summary>
    /// 分享链接与邀请相关 API (/v1/share)
    /// </summary>
    public static class ShareApi
    {
        /// <summary>
        /// 解析分享链接/Key 获取会话信息 (POST /v1/share/info)
        /// </summary>
        public static async Task<ShareInfoResult> GetShareInfoAsync(string token, string key, string ts = "")
        {
            var result = new ShareInfoResult();
            if (string.IsNullOrEmpty(key))
            {
                result.Code = -1;
                result.Msg = "分享 Key 为空";
                return result;
            }

            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("key", JsonValue.CreateStringValue(key.Trim()));
                if (!string.IsNullOrEmpty(ts))
                {
                    reqObj.SetNamedValue("ts", JsonValue.CreateStringValue(ts.Trim()));
                }

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/share/info", reqObj.Stringify(), token);
                AppLogger.Log("ShareApi", "GetShareInfoAsync response: " + jsonStr);

                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");

                    if (root.ContainsKey("data") && root.GetNamedValue("data").ValueType == JsonValueType.Object)
                    {
                        var dataObj = root.GetNamedObject("data");
                        if (dataObj.ContainsKey("share") && dataObj.GetNamedValue("share").ValueType == JsonValueType.Object)
                        {
                            var shareObj = dataObj.GetNamedObject("share");
                            var item = new ShareInfoItem();

                            if (shareObj.ContainsKey("id")) item.Id = (long)shareObj.GetNamedNumber("id");
                            if (shareObj.ContainsKey("user_id")) item.UserId = shareObj.GetNamedString("user_id");
                            if (shareObj.ContainsKey("chat_name")) item.ChatName = shareObj.GetNamedString("chat_name");
                            if (shareObj.ContainsKey("chat_type")) item.ChatType = (int)shareObj.GetNamedNumber("chat_type");
                            if (shareObj.ContainsKey("chat_id")) item.ChatId = shareObj.GetNamedString("chat_id");
                            if (shareObj.ContainsKey("key")) item.Key = shareObj.GetNamedString("key");
                            if (shareObj.ContainsKey("create_by")) item.CreateBy = shareObj.GetNamedString("create_by");
                            if (shareObj.ContainsKey("create_time")) item.CreateTime = (long)shareObj.GetNamedNumber("create_time");
                            if (shareObj.ContainsKey("imageUrl")) item.ImageUrl = shareObj.GetNamedString("imageUrl");
                            if (shareObj.ContainsKey("imageName")) item.ImageName = shareObj.GetNamedString("imageName");

                            result.Share = item;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "解析分享链接异常: " + ex.Message;
                AppLogger.Log("ShareApi", "GetShareInfoAsync error: " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// 创建分享链接 (POST /v1/share/create)
        /// </summary>
        public static async Task<CreateShareResult> CreateShareAsync(string token, string chatId, int chatType, string chatName)
        {
            var result = new CreateShareResult();
            if (string.IsNullOrEmpty(token))
            {
                result.Code = -1;
                result.Msg = "Token 为空";
                return result;
            }

            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("chatId", JsonValue.CreateStringValue(chatId));
                reqObj.SetNamedValue("chatType", JsonValue.CreateNumberValue(chatType));
                reqObj.SetNamedValue("chatName", JsonValue.CreateStringValue(chatName));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/share/create", reqObj.Stringify(), token);
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");

                    if (root.ContainsKey("data") && root.GetNamedValue("data").ValueType == JsonValueType.Object)
                    {
                        var dataObj = root.GetNamedObject("data");
                        if (dataObj.ContainsKey("key")) result.Key = dataObj.GetNamedString("key");
                        if (dataObj.ContainsKey("shareUrl")) result.ShareUrl = dataObj.GetNamedString("shareUrl");
                        if (dataObj.ContainsKey("imageKey")) result.ImageKey = dataObj.GetNamedString("imageKey");
                        if (dataObj.ContainsKey("ts")) result.Ts = (long)dataObj.GetNamedNumber("ts");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "创建分享链接异常: " + ex.Message;
            }

            return result;
        }
    }
}
