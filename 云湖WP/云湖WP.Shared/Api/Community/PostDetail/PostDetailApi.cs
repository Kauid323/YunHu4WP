using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;
using 云湖WP.Api.Community.Board;

namespace 云湖WP.Api.Community.PostDetail
{
    /// <summary>
    /// 社区动态详情与评论 API 接口 (/v1/community/posts/post-detail, /v1/community/comment/*)
    /// </summary>
    public static class PostDetailApi
    {
        /// <summary>
        /// 获取文章详情 (POST /v1/community/posts/post-detail)
        /// </summary>
        public static async Task<CommunityPostDetailResult> GetPostDetailAsync(string token, long postId)
        {
            var result = new CommunityPostDetailResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("id", JsonValue.CreateNumberValue(postId));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/posts/post-detail", reqObj.Stringify(), token);
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
                        if (data.ContainsKey("ba") && data.GetNamedValue("ba").ValueType == JsonValueType.Object)
                        {
                            result.Board = BoardApi.ParseBoardItem(data.GetNamedObject("ba"));
                        }

                        if (data.ContainsKey("post") && data.GetNamedValue("post").ValueType == JsonValueType.Object)
                        {
                            result.Post = ParsePostItem(data.GetNamedObject("post"));
                        }
                        else
                        {
                            result.Post = ParsePostItem(data);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取动态详情失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 获取文章评论列表 (POST /v1/community/comment/comment-list)
        /// </summary>
        public static async Task<CommunityCommentListResult> GetCommentListAsync(string token, long postId, int page = 1, int size = 20)
        {
            var result = new CommunityCommentListResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("postId", JsonValue.CreateNumberValue(postId));
                reqObj.SetNamedValue("page", JsonValue.CreateNumberValue(page));
                reqObj.SetNamedValue("size", JsonValue.CreateNumberValue(size));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/comment/comment-list", reqObj.Stringify(), token);
                ParseCommentsResponse(jsonStr, result);
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取评论失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 发表评论 (POST /v1/community/comment/comment)
        /// </summary>
        public static async Task<ApiResult> SendCommentAsync(string token, long postId, string content, long commentId = 0)
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("postId", JsonValue.CreateNumberValue(postId));
                reqObj.SetNamedValue("commentId", JsonValue.CreateNumberValue(commentId));
                reqObj.SetNamedValue("content", JsonValue.CreateStringValue(content));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/comment/comment", reqObj.Stringify(), token);
                if (!string.IsNullOrEmpty(jsonStr))
                {
                    JsonObject root;
                    if (JsonObject.TryParse(jsonStr, out root))
                    {
                        result.Code = SafeGetInt(root, "code", -1);
                        result.Msg = SafeGetString(root, "msg", "");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "发送评论失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 点赞/取消点赞动态 (POST /v1/community/posts/post-like)
        /// </summary>
        public static async Task<ApiResult> TogglePostLikeAsync(string token, long postId)
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("id", JsonValue.CreateNumberValue(postId));
                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/posts/post-like", reqObj.Stringify(), token);
                if (!string.IsNullOrEmpty(jsonStr))
                {
                    JsonObject root;
                    if (JsonObject.TryParse(jsonStr, out root))
                    {
                        result.Code = SafeGetInt(root, "code", -1);
                        result.Msg = SafeGetString(root, "msg", "");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "点赞操作失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 收藏/取消收藏动态 (POST /v1/community/posts/post-collect)
        /// </summary>
        public static async Task<ApiResult> TogglePostCollectAsync(string token, long postId)
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("id", JsonValue.CreateNumberValue(postId));
                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/posts/post-collect", reqObj.Stringify(), token);
                if (!string.IsNullOrEmpty(jsonStr))
                {
                    JsonObject root;
                    if (JsonObject.TryParse(jsonStr, out root))
                    {
                        result.Code = SafeGetInt(root, "code", -1);
                        result.Msg = SafeGetString(root, "msg", "");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "收藏操作失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 给动态投币打赏 (POST /v1/community/posts/post-reward)
        /// </summary>
        public static async Task<ApiResult> RewardPostAsync(string token, long postId, string recvId, double amount)
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("postId", JsonValue.CreateNumberValue(postId));
                reqObj.SetNamedValue("recvId", JsonValue.CreateStringValue(recvId ?? ""));
                reqObj.SetNamedValue("amount", JsonValue.CreateNumberValue(amount));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/posts/post-reward", reqObj.Stringify(), token);
                if (!string.IsNullOrEmpty(jsonStr))
                {
                    JsonObject root;
                    if (JsonObject.TryParse(jsonStr, out root))
                    {
                        result.Code = SafeGetInt(root, "code", -1);
                        result.Msg = SafeGetString(root, "msg", "");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "打赏投币失败: " + ex.Message;
            }
            return result;
        }

        private static void ParseCommentsResponse(string jsonStr, CommunityCommentListResult result)
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

                    JsonArray arr = null;
                    if (data.ContainsKey("comments") && data.GetNamedValue("comments").ValueType == JsonValueType.Array)
                    {
                        arr = data.GetNamedArray("comments");
                    }
                    else if (data.ContainsKey("list") && data.GetNamedValue("list").ValueType == JsonValueType.Array)
                    {
                        arr = data.GetNamedArray("list");
                    }

                    if (arr != null)
                    {
                        for (uint i = 0; i < arr.Count; i++)
                        {
                            try
                            {
                                var commentObj = arr.GetObjectAt(i);
                                var item = ParseCommentItem(commentObj);
                                if (item != null)
                                {
                                    result.Comments.Add(item);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        private static CommunityCommentItem ParseCommentItem(JsonObject obj)
        {
            if (obj == null) return null;
            var item = new CommunityCommentItem();
            item.Id = SafeGetLong(obj, "id", 0);
            item.PostId = SafeGetLong(obj, "postId", 0);
            item.ParentId = SafeGetLong(obj, "parentId", 0);
            item.SenderId = SafeGetSenderId(obj);
            item.SenderNickname = SafeGetSenderNickname(obj);
            item.SenderAvatar = SafeGetSenderAvatar(obj);
            item.Content = SafeGetString(obj, "content", "");
            item.CreateTimeText = SafeGetString(obj, "createTimeText", "");
            item.CreateTime = SafeGetLong(obj, "createTime", 0);
            item.LikeNum = SafeGetInt(obj, "likeNum", 0);
            item.AmountNum = SafeGetDouble(obj, "amountNum", 0);
            item.IsLiked = SafeGetBool(obj, "isLiked", false);
            return item;
        }

        private static CommunityPostItem ParsePostItem(JsonObject obj)
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

        private static bool SafeGetBool(JsonObject obj, string key, bool defaultVal = false)
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
