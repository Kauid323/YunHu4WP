using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Community
{
    /// <summary>
    /// 社区/动态 API 接口 (/v1/community/posts)
    /// </summary>
    public static class CommunityApi
    {
        /// <summary>
        /// 获取文章动态列表 (POST /v1/community/posts/post-list)
        /// </summary>
        /// <param name="token">用户登录凭证</param>
        /// <param name="typ">排序筛选: 4-最新发表, 3-最新回复, 1-综合</param>
        /// <param name="baId">分区/板块 ID (0 为全站)</param>
        /// <param name="page">页码</param>
        /// <param name="size">每页数量</param>
        public static async Task<CommunityPostListResult> GetPostListAsync(string token, int typ = 4, int baId = 0, int page = 1, int size = 20)
        {
            var result = new CommunityPostListResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("page", JsonValue.CreateNumberValue(page));
                reqObj.SetNamedValue("size", JsonValue.CreateNumberValue(size));
                if (typ > 0)
                {
                    reqObj.SetNamedValue("typ", JsonValue.CreateNumberValue(typ));
                }
                if (baId > 0)
                {
                    reqObj.SetNamedValue("baId", JsonValue.CreateNumberValue(baId));
                }

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/posts/post-list", reqObj.Stringify(), token);
                ParsePostsResponse(jsonStr, result);
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取动态失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 获取热门/推荐动态列表 (POST /v1/community/posts/post-list-recommend)
        /// </summary>
        /// <param name="token">用户登录凭证</param>
        /// <param name="page">页码</param>
        /// <param name="size">每页数量</param>
        public static async Task<CommunityPostListResult> GetRecommendPostListAsync(string token, int page = 1, int size = 20)
        {
            var result = new CommunityPostListResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("page", JsonValue.CreateNumberValue(page));
                reqObj.SetNamedValue("size", JsonValue.CreateNumberValue(size));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/posts/post-list-recommend", reqObj.Stringify(), token);
                ParsePostsResponse(jsonStr, result);
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取热门动态失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 解析社区文章列表通用响应 JSON
        /// </summary>
        private static void ParsePostsResponse(string jsonStr, CommunityPostListResult result)
        {
            if (string.IsNullOrEmpty(jsonStr))
            {
                result.Code = -1;
                result.Msg = "响应为空";
                return;
            }

            JsonObject root;
            if (!JsonObject.TryParse(jsonStr, out root))
            {
                result.Code = -1;
                result.Msg = "JSON 解析失败";
                return;
            }

            result.Code = SafeGetInt(root, "code", -1);
            result.Msg = SafeGetString(root, "msg", "");

            if (result.IsSuccess && root.ContainsKey("data"))
            {
                var dataVal = root.GetNamedValue("data");
                if (dataVal.ValueType == JsonValueType.Object)
                {
                    var data = dataVal.GetObject();
                    result.Total = SafeGetInt(data, "total", 0);

                    JsonArray postsArray = null;
                    if (data.ContainsKey("posts") && data.GetNamedValue("posts").ValueType == JsonValueType.Array)
                    {
                        postsArray = data.GetNamedArray("posts");
                    }
                    else if (data.ContainsKey("list") && data.GetNamedValue("list").ValueType == JsonValueType.Array)
                    {
                        postsArray = data.GetNamedArray("list");
                    }

                    if (postsArray != null)
                    {
                        for (uint i = 0; i < postsArray.Count; i++)
                        {
                            try
                            {
                                var postVal = postsArray.GetObjectAt(i);
                                var item = ParsePostItem(postVal);
                                if (item != null)
                                {
                                    result.Posts.Add(item);
                                }
                            }
                            catch { }
                        }
                    }
                }
                else if (dataVal.ValueType == JsonValueType.Array)
                {
                    var postsArray = dataVal.GetArray();
                    for (uint i = 0; i < postsArray.Count; i++)
                    {
                        try
                        {
                            var postVal = postsArray.GetObjectAt(i);
                            var item = ParsePostItem(postVal);
                            if (item != null)
                            {
                                result.Posts.Add(item);
                            }
                        }
                        catch { }
                    }
                }
            }
        }

        /// <summary>
        /// 解析单条文章数据
        /// </summary>
        public static CommunityPostItem ParsePostItem(JsonObject obj)
        {
            if (obj == null) return null;
            var item = new CommunityPostItem();

            item.Id = SafeGetLong(obj, "id", 0);
            item.BaId = SafeGetInt(obj, "baId", 0);
            item.SenderId = SafeGetSenderId(obj);
            item.SenderNickname = SafeGetSenderNickname(obj);
            item.SenderAvatar = SafeGetSenderAvatar(obj);
            item.Title = SafeGetString(obj, "title", "");
            item.Content = SafeGetString(obj, "content", "");
            item.ContentType = SafeGetInt(obj, "contentType", 1);
            item.CreateTimeText = SafeGetString(obj, "createTimeText", "");
            item.CreateTime = SafeGetLong(obj, "createTime", 0);

            item.LikeNum = SafeGetInt(obj, "likeNum", 0);
            item.CommentNum = SafeGetInt(obj, "commentNum", 0);
            item.CollectNum = SafeGetInt(obj, "collectNum", 0);
            item.AmountNum = SafeGetDouble(obj, "amountNum", 0);

            item.IsLiked = SafeGetBool(obj, "isLiked", false);
            item.IsCollected = SafeGetBool(obj, "isCollected", false);
            item.IsReward = SafeGetBool(obj, "isReward", false);

            return item;
        }

        public static string SafeGetSenderId(JsonObject obj)
        {
            if (obj == null) return "";
            string[] directKeys = new string[] { "senderId", "sender_id", "userId", "user_id", "uid", "createBy", "create_by", "authorId", "author_id", "creatorId", "creator_id", "send_id", "sendId" };
            string id = SafeGetDirectString(obj, directKeys);
            if (!string.IsNullOrEmpty(id)) return id;

            string[] subObjNames = new string[] { "user", "author", "sender", "creator", "userInfo", "user_info", "member" };
            foreach (var subName in subObjNames)
            {
                if (obj.ContainsKey(subName) && obj.GetNamedValue(subName).ValueType == JsonValueType.Object)
                {
                    var sub = obj.GetNamedObject(subName);
                    string subId = SafeGetDirectString(sub, "id", "userId", "user_id", "uid", "senderId", "sender_id");
                    if (!string.IsNullOrEmpty(subId)) return subId;
                }
            }
            return "";
        }

        public static string SafeGetSenderNickname(JsonObject obj)
        {
            if (obj == null) return "";
            string[] directKeys = new string[] { "senderNickname", "sender_nickname", "nickname", "nick_name", "userName", "user_name", "authorName", "author_name", "author" };
            string name = SafeGetDirectString(obj, directKeys);
            if (!string.IsNullOrEmpty(name)) return name;

            string[] subObjNames = new string[] { "user", "author", "sender", "creator", "userInfo", "user_info", "member" };
            foreach (var subName in subObjNames)
            {
                if (obj.ContainsKey(subName) && obj.GetNamedValue(subName).ValueType == JsonValueType.Object)
                {
                    var sub = obj.GetNamedObject(subName);
                    string subNameVal = SafeGetDirectString(sub, "name", "nickname", "nick_name", "userName", "user_name", "displayName", "display_name");
                    if (!string.IsNullOrEmpty(subNameVal)) return subNameVal;
                }
            }
            return "";
        }

        public static string SafeGetSenderAvatar(JsonObject obj)
        {
            if (obj == null) return "";
            string[] directKeys = new string[] { "senderAvatar", "sender_avatar", "avatar", "avatarUrl", "avatar_url", "userAvatar", "user_avatar", "headImg", "head_img" };
            string avatar = SafeGetDirectString(obj, directKeys);
            if (!string.IsNullOrEmpty(avatar)) return avatar;

            string[] subObjNames = new string[] { "user", "author", "sender", "creator", "userInfo", "user_info", "member" };
            foreach (var subName in subObjNames)
            {
                if (obj.ContainsKey(subName) && obj.GetNamedValue(subName).ValueType == JsonValueType.Object)
                {
                    var sub = obj.GetNamedObject(subName);
                    string subAvatar = SafeGetDirectString(sub, "avatar", "avatarUrl", "avatar_url", "headImg", "head_img", "url");
                    if (!string.IsNullOrEmpty(subAvatar)) return subAvatar;
                }
            }
            return "";
        }

        private static string SafeGetDirectString(JsonObject obj, params string[] keys)
        {
            if (obj == null) return "";
            foreach (var k in keys)
            {
                if (obj.ContainsKey(k))
                {
                    var val = obj.GetNamedValue(k);
                    if (val.ValueType == JsonValueType.String)
                    {
                        string s = val.GetString();
                        if (!string.IsNullOrEmpty(s)) return s;
                    }
                    if (val.ValueType == JsonValueType.Number)
                    {
                        double d = val.GetNumber();
                        if (d > 0) return ((long)d).ToString();
                    }
                }
            }
            return "";
        }

        public static string SafeGetString(JsonObject obj, string key, string defaultVal = "")
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

        public static long SafeGetLong(JsonObject obj, string key, long defaultVal = 0)
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

        public static int SafeGetInt(JsonObject obj, string key, int defaultVal = 0)
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

        public static double SafeGetDouble(JsonObject obj, string key, double defaultVal = 0)
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

        public static bool SafeGetBool(JsonObject obj, string key, bool defaultVal = false)
        {
            if (obj != null && obj.ContainsKey(key))
            {
                var val = obj.GetNamedValue(key);
                if (val.ValueType == JsonValueType.Boolean) return val.GetBoolean();
                if (val.ValueType == JsonValueType.Number) return val.GetNumber() == 1;
                if (val.ValueType == JsonValueType.String)
                {
                    string s = val.GetString();
                    return s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
                }
            }
            return defaultVal;
        }
    }
}
