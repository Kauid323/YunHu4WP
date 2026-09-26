using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.WebApi.User
{
    /// <summary>
    /// 云湖 Web API 用户接口 (chat-web-go.jwzhd.com)
    /// </summary>
    public static class UserWebApi
    {
        /// <summary>
        /// 获取用户主页信息 (GET /v1/user/homepage?userId=...)
        /// </summary>
        public static async Task<UserWebHomepageResult> GetUserHomepageAsync(string userId, string token = null)
        {
            var result = new UserWebHomepageResult();
            if (string.IsNullOrEmpty(userId))
            {
                result.Code = -1;
                result.Msg = "用户ID为空";
                return result;
            }

            try
            {
                string url = YunhuApiConfig.WebBaseUrl + "/v1/user/homepage?userId=" + Uri.EscapeDataString(userId);
                string jsonStr = await HttpHelper.GetAsync(url, token);

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
                        if (data.ContainsKey("user") && data.GetNamedValue("user").ValueType == JsonValueType.Object)
                        {
                            result.User = ParseUserHomepage(data.GetNamedObject("user"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取用户主页信息失败: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 获取用户自身信息 (GET /v1/user/info)
        /// </summary>
        public static async Task<UserWebSelfInfoResult> GetSelfInfoAsync(string token)
        {
            var result = new UserWebSelfInfoResult();
            if (string.IsNullOrEmpty(token))
            {
                result.Code = -1;
                result.Msg = "Token 为空";
                return result;
            }

            try
            {
                string url = YunhuApiConfig.WebBaseUrl + "/v1/user/info";
                string jsonStr = await HttpHelper.GetAsync(url, token);

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
                        if (data.ContainsKey("user") && data.GetNamedValue("user").ValueType == JsonValueType.Object)
                        {
                            var userObj = data.GetNamedObject("user");
                            result.UserId = SafeGetString(userObj, "userId", "");
                            result.Nickname = SafeGetString(userObj, "nickname", "");
                            result.Phone = SafeGetString(userObj, "phone", "");
                            result.AvatarId = SafeGetString(userObj, "avatarId", "");
                            result.AvatarUrl = SafeGetString(userObj, "avatarUrl", "");
                            result.GoldCoinAmount = SafeGetDouble(userObj, "goldCoinAmount", 0);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取用户自身信息失败: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 解析用户主页数据对象
        /// </summary>
        public static UserWebHomepage ParseUserHomepage(JsonObject obj)
        {
            if (obj == null) return null;
            var item = new UserWebHomepage();

            item.UserId = SafeGetString(obj, "userId", "");
            item.Nickname = SafeGetString(obj, "nickname", "");
            item.AvatarUrl = SafeGetString(obj, "avatarUrl", "");
            item.RegisterTime = SafeGetLong(obj, "registerTime", 0);
            item.RegisterTimeText = SafeGetString(obj, "registerTimeText", "");
            item.OnLineDay = SafeGetInt(obj, "onLineDay", 0);
            item.ContinuousOnLineDay = SafeGetInt(obj, "continuousOnLineDay", 0);
            item.IsVip = SafeGetInt(obj, "isVip", 0);

            if (obj.ContainsKey("medals") && obj.GetNamedValue("medals").ValueType == JsonValueType.Array)
            {
                var arr = obj.GetNamedArray("medals");
                for (uint i = 0; i < arr.Count; i++)
                {
                    try
                    {
                        var mObj = arr.GetObjectAt(i);
                        var medal = new UserWebMedal
                        {
                            Id = SafeGetInt(mObj, "id", 0),
                            Name = SafeGetString(mObj, "name", ""),
                            Desc = SafeGetString(mObj, "desc", ""),
                            ImageUrl = SafeGetString(mObj, "imageUrl", ""),
                            Sort = SafeGetInt(mObj, "sort", 0)
                        };
                        item.Medals.Add(medal);
                    }
                    catch { }
                }
            }

            return item;
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

        private static double SafeGetDouble(JsonObject obj, string key, double defaultVal = 0)
        {
            if (obj != null && obj.ContainsKey(key))
            {
                var val = obj.GetNamedValue(key);
                if (val.ValueType == JsonValueType.Number) return val.GetNumber();
                if (val.ValueType == JsonValueType.String)
                {
                    double num;
                    if (double.TryParse(val.GetString(), out num)) return num;
                }
            }
            return defaultVal;
        }
    }
}
