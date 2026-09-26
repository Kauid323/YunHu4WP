using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Community.Board
{
    /// <summary>
    /// 社区板块/分区 API 接口 (/v1/community/ba/*)
    /// </summary>
    public static class BoardApi
    {
        /// <summary>
        /// 获取板块信息 (POST /v1/community/ba/info)
        /// </summary>
        public static async Task<BoardInfoResult> GetBoardInfoAsync(string token, int baId)
        {
            var result = new BoardInfoResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("id", JsonValue.CreateNumberValue(baId));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/ba/info", reqObj.Stringify(), token);
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
                            result.Board = ParseBoardItem(data.GetNamedObject("ba"));
                        }
                        else
                        {
                            result.Board = ParseBoardItem(data);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取板块详情失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 获取板块动态列表 (POST /v1/community/posts/post-list)
        /// </summary>
        /// <param name="token">登录凭证</param>
        /// <param name="baId">板块 ID</param>
        /// <param name="typ">1-最新, 2-最热/热门</param>
        /// <param name="page">页码</param>
        /// <param name="size">每页条数</param>
        public static async Task<CommunityPostListResult> GetBoardPostsAsync(string token, int baId, int typ = 2, int page = 1, int size = 20)
        {
            var result = new CommunityPostListResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("baId", JsonValue.CreateNumberValue(baId));
                reqObj.SetNamedValue("typ", JsonValue.CreateNumberValue(typ));
                reqObj.SetNamedValue("page", JsonValue.CreateNumberValue(page));
                reqObj.SetNamedValue("size", JsonValue.CreateNumberValue(size));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/posts/post-list", reqObj.Stringify(), token);
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
                                    var postObj = postsArray.GetObjectAt(i);
                                    var item = CommunityApi.ParsePostItem(postObj);
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
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取板块动态失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 关注板块 (POST /v1/community/ba/user-follow-ba)
        /// </summary>
        public static async Task<ApiResult> FollowBoardAsync(string token, int baId)
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("baId", JsonValue.CreateNumberValue(baId));
                reqObj.SetNamedValue("followSource", JsonValue.CreateNumberValue(2));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/ba/user-follow-ba", reqObj.Stringify(), token);
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
                result.Msg = "关注板块失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 取消关注板块 (POST /v1/community/ba/user-unfollow-ba)
        /// </summary>
        public static async Task<ApiResult> UnfollowBoardAsync(string token, int baId)
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("baId", JsonValue.CreateNumberValue(baId));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/ba/user-unfollow-ba", reqObj.Stringify(), token);
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
                result.Msg = "取消关注失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 获取板块关注者/管理员/成员列表 (POST /v1/community/ba/follower-list)
        /// </summary>
        public static async Task<BoardFollowerListResult> GetBoardFollowersAsync(string token, int baId, int page = 1, int size = 50, string memberName = "")
        {
            var result = new BoardFollowerListResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("id", JsonValue.CreateNumberValue(baId));
                reqObj.SetNamedValue("page", JsonValue.CreateNumberValue(page));
                reqObj.SetNamedValue("size", JsonValue.CreateNumberValue(size));
                reqObj.SetNamedValue("memberName", JsonValue.CreateStringValue(memberName ?? ""));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/ba/follower-list", reqObj.Stringify(), token);
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
                        result.Total = SafeGetInt(data, "total", 0);

                        JsonArray arr = null;
                        if (data.ContainsKey("followers") && data.GetNamedValue("followers").ValueType == JsonValueType.Array)
                        {
                            arr = data.GetNamedArray("followers");
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
                                    var fObj = arr.GetObjectAt(i);
                                    var item = ParseFollowerItem(fObj);
                                    if (item != null)
                                    {
                                        result.Followers.Add(item);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取关注者列表失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 解析板块对象
        /// </summary>
        public static BoardInfoItem ParseBoardItem(JsonObject obj)
        {
            if (obj == null) return null;
            var item = new BoardInfoItem();

            item.Id = SafeGetInt(obj, "id", 0);
            item.Name = SafeGetString(obj, "name", "");
            item.Avatar = SafeGetString(obj, "avatar", "");
            item.DelTime = SafeGetLong(obj, "delTime", 0);
            item.CreateTime = SafeGetLong(obj, "createTime", 0);
            item.LastActive = SafeGetLong(obj, "lastActive", 0);
            item.MemberNum = SafeGetInt(obj, "memberNum", 0);
            item.PostNum = SafeGetInt(obj, "postNum", 0);
            item.GroupNum = SafeGetInt(obj, "groupNum", 0);
            item.CreateTimeText = SafeGetString(obj, "createTimeText", "");
            item.IsFollowed = SafeGetBool(obj, "isFollowed", false);

            // 提取创建者 ID (createBy / create_by / userId / user_id)
            string createBy = SafeGetString(obj, "createBy", "");
            if (string.IsNullOrEmpty(createBy)) createBy = SafeGetString(obj, "create_by", "");
            if (string.IsNullOrEmpty(createBy)) createBy = SafeGetString(obj, "userId", "");
            if (string.IsNullOrEmpty(createBy)) createBy = SafeGetString(obj, "user_id", "");
            item.CreateBy = createBy;

            return item;
        }

        /// <summary>
        /// 解析关注者/成员对象
        /// </summary>
        public static BoardFollowerItem ParseFollowerItem(JsonObject obj)
        {
            if (obj == null) return null;
            var item = new BoardFollowerItem();

            item.Id = SafeGetInt(obj, "id", 0);
            item.BaId = SafeGetInt(obj, "baId", 0);
            item.UserId = SafeGetString(obj, "userId", "");
            if (string.IsNullOrEmpty(item.UserId)) item.UserId = SafeGetString(obj, "user_id", "");
            item.Nickname = SafeGetString(obj, "nickname", "");
            if (string.IsNullOrEmpty(item.Nickname)) item.Nickname = SafeGetString(obj, "name", "");
            item.AvatarUrl = SafeGetString(obj, "avatarUrl", "");
            if (string.IsNullOrEmpty(item.AvatarUrl)) item.AvatarUrl = SafeGetString(obj, "avatar_url", "");
            if (string.IsNullOrEmpty(item.AvatarUrl)) item.AvatarUrl = SafeGetString(obj, "avatar", "");
            item.UserLevel = SafeGetInt(obj, "userLevel", 0);
            item.CreateTime = SafeGetLong(obj, "createTime", 0);
            item.VipUserId = SafeGetString(obj, "vipUserid", "");
            item.VipEndTime = SafeGetLong(obj, "vipEndTime", 0);

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
