using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;
using 云湖WP.Api.Community;

namespace 云湖WP.Api.Community.MyActivity
{
    /// <summary>
    /// 动态管理相关 API (我的动态、我的板块、我的关注、我的收藏)
    /// </summary>
    public static class MyActivityApi
    {
        // ────────────────────────────────────────────────
        // 我的动态列表  POST /v1/community/posts/my-post-list
        // ────────────────────────────────────────────────
        public static async Task<MyPostListResult> GetMyPostListAsync(
            string token, int page = 1, int size = 20)
        {
            var result = new MyPostListResult();
            if (string.IsNullOrEmpty(token)) { result.Code = -1; result.Msg = "Token 为空"; return result; }
            try
            {
                var req = new JsonObject();
                req.SetNamedValue("size", JsonValue.CreateNumberValue(size));
                req.SetNamedValue("page", JsonValue.CreateNumberValue(page));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/posts/my-post-list", req.Stringify(), token);
                JsonObject root;
                if (!JsonObject.TryParse(jsonStr ?? "", out root)) { result.Code = -1; result.Msg = "解析失败"; return result; }

                result.Code = SafeGetInt(root, "code", -1);
                result.Msg = SafeGetString(root, "msg", "");
                if (result.IsSuccess && root.ContainsKey("data"))
                {
                    var data = root.GetNamedValue("data").GetObject();
                    result.Total = SafeGetInt(data, "total", 0);
                    if (data.ContainsKey("posts") && data.GetNamedValue("posts").ValueType == JsonValueType.Array)
                    {
                        foreach (var item in data.GetNamedArray("posts"))
                        {
                            var post = ParsePostItem(item.GetObject());
                            result.Posts.Add(post);
                        }
                    }
                }
            }
            catch (Exception ex) { result.Code = -1; result.Msg = ex.Message; }
            return result;
        }

        // ────────────────────────────────────────────────
        // 我的板块列表  POST /v1/community/ba/list-by-create
        // ────────────────────────────────────────────────
        public static async Task<MyBoardListResult> GetMyBoardListAsync(string token, string userId)
        {
            var result = new MyBoardListResult();
            if (string.IsNullOrEmpty(token)) { result.Code = -1; result.Msg = "Token 为空"; return result; }
            try
            {
                var req = new JsonObject();
                req.SetNamedValue("userId", JsonValue.CreateStringValue(userId ?? ""));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/ba/list-by-create", req.Stringify(), token);
                JsonObject root;
                if (!JsonObject.TryParse(jsonStr ?? "", out root)) { result.Code = -1; result.Msg = "解析失败"; return result; }

                result.Code = SafeGetInt(root, "code", -1);
                result.Msg = SafeGetString(root, "msg", "");
                if (result.IsSuccess && root.ContainsKey("data"))
                {
                    var data = root.GetNamedValue("data").GetObject();
                    if (data.ContainsKey("ba") && data.GetNamedValue("ba").ValueType == JsonValueType.Array)
                    {
                        foreach (var item in data.GetNamedArray("ba"))
                        {
                            var obj = item.GetObject();
                            result.Boards.Add(new MyBoardItem
                            {
                                Id = SafeGetInt(obj, "id", 0),
                                Name = SafeGetString(obj, "name", ""),
                                Avatar = SafeGetString(obj, "avatar", ""),
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { result.Code = -1; result.Msg = ex.Message; }
            return result;
        }

        // ────────────────────────────────────────────────
        // 我的关注板块  POST /v1/community/ba/following-ba-list (typ=1)
        // ────────────────────────────────────────────────
        public static async Task<MyFollowingBoardListResult> GetMyFollowingBoardListAsync(
            string token, int page = 1, int size = 20)
        {
            var result = new MyFollowingBoardListResult();
            if (string.IsNullOrEmpty(token)) { result.Code = -1; result.Msg = "Token 为空"; return result; }
            try
            {
                var req = new JsonObject();
                req.SetNamedValue("typ", JsonValue.CreateNumberValue(1)); // 1=关注
                req.SetNamedValue("size", JsonValue.CreateNumberValue(size));
                req.SetNamedValue("page", JsonValue.CreateNumberValue(page));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/ba/following-ba-list", req.Stringify(), token);
                JsonObject root;
                if (!JsonObject.TryParse(jsonStr ?? "", out root)) { result.Code = -1; result.Msg = "解析失败"; return result; }

                result.Code = SafeGetInt(root, "code", -1);
                result.Msg = SafeGetString(root, "msg", "");
                if (result.IsSuccess && root.ContainsKey("data"))
                {
                    var data = root.GetNamedValue("data").GetObject();
                    result.Total = SafeGetInt(data, "total", 0);
                    if (data.ContainsKey("ba") && data.GetNamedValue("ba").ValueType == JsonValueType.Array)
                    {
                        foreach (var item in data.GetNamedArray("ba"))
                        {
                            var obj = item.GetObject();
                            result.Boards.Add(new FollowingBoardItem
                            {
                                Id = SafeGetInt(obj, "id", 0),
                                Name = SafeGetString(obj, "name", ""),
                                Avatar = SafeGetString(obj, "avatar", ""),
                                MemberNum = SafeGetInt(obj, "memberNum", 0),
                                PostNum = SafeGetInt(obj, "postNum", 0),
                                CreateTimeText = SafeGetString(obj, "createTimeText", ""),
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { result.Code = -1; result.Msg = ex.Message; }
            return result;
        }

        // ────────────────────────────────────────────────
        // 我的收藏  POST /v1/community/posts/post-list (typ=1, isCollect=1)
        // API 文档未见专门收藏接口，使用普通 post-list 接口暂时无法单独过滤收藏。
        // 实际上 my-post-list 中可能不包含收藏，这里使用 collect 字段过滤。
        // 用 post-list 获取推荐，客户端过滤 isCollected=true 的；
        // 或使用 post-list typ=1+baId=0，具体看服务端是否支持。
        // 保守做法：用 my-post-list 返回的数据中 IsCollected=true 的条目（实际是自己发布的已收藏文章）
        // 更合理做法：调用 post-list 不带 baId，只用 page/size，然后展示 isCollected 过滤的。
        // 但文档中没有专门的"收藏列表"端点，所以我们只展示已发布的+已收藏的。
        // 这里提供一个通用 GetPostListAsync 供收藏 Tab 使用。
        public static async Task<MyCollectListResult> GetMyCollectListAsync(
            string token, int page = 1, int size = 20)
        {
            var result = new MyCollectListResult();
            if (string.IsNullOrEmpty(token)) { result.Code = -1; result.Msg = "Token 为空"; return result; }
            try
            {
                // 使用 post-list-recommend 接口获取推荐文章并过滤收藏
                var req = new JsonObject();
                req.SetNamedValue("size", JsonValue.CreateNumberValue(size));
                req.SetNamedValue("page", JsonValue.CreateNumberValue(page));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/posts/post-list-recommend", req.Stringify(), token);
                JsonObject root;
                if (!JsonObject.TryParse(jsonStr ?? "", out root)) { result.Code = -1; result.Msg = "解析失败"; return result; }

                result.Code = SafeGetInt(root, "code", -1);
                result.Msg = SafeGetString(root, "msg", "");
                if (result.IsSuccess && root.ContainsKey("data"))
                {
                    var data = root.GetNamedValue("data").GetObject();
                    result.Total = SafeGetInt(data, "total", 0);
                    if (data.ContainsKey("posts") && data.GetNamedValue("posts").ValueType == JsonValueType.Array)
                    {
                        foreach (var item in data.GetNamedArray("posts"))
                        {
                            var post = ParsePostItem(item.GetObject());
                            if (post.IsCollected)
                            {
                                result.Posts.Add(post);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { result.Code = -1; result.Msg = ex.Message; }
            return result;
        }

        // ────────────────────────────────────────────────
        // 工具方法
        // ────────────────────────────────────────────────
        private static CommunityPostItem ParsePostItem(JsonObject obj)
        {
            var item = new CommunityPostItem();
            item.Id = SafeGetLong(obj, "id", 0);
            item.BaId = SafeGetInt(obj, "baId", 0);
            item.SenderId = SafeGetString(obj, "senderId", "");
            item.SenderNickname = SafeGetString(obj, "senderNickname", "");
            item.SenderAvatar = SafeGetString(obj, "senderAvatar", "");
            item.Title = SafeGetString(obj, "title", "");
            item.Content = SafeGetString(obj, "content", "");
            item.ContentType = SafeGetInt(obj, "contentType", 1);
            item.CreateTimeText = SafeGetString(obj, "createTimeText", "");
            item.CreateTime = SafeGetLong(obj, "createTime", 0);
            item.LikeNum = SafeGetInt(obj, "likeNum", 0);
            item.CommentNum = SafeGetInt(obj, "commentNum", 0);
            item.CollectNum = SafeGetInt(obj, "collectNum", 0);
            item.AmountNum = SafeGetDouble(obj, "amountNum", 0);
            item.IsLiked = SafeGetInt(obj, "isLiked", 0) == 1 || SafeGetString(obj, "isLiked", "0") == "1";
            item.IsCollected = SafeGetInt(obj, "isCollected", 0) == 1;
            item.IsReward = SafeGetInt(obj, "isReward", 0) == 1;
            item.IsDraft = SafeGetInt(obj, "isDraft", 0) == 1;
            item.IsSticky = SafeGetInt(obj, "isSticky", 0);
            return item;
        }

        internal static string SafeGetString(JsonObject obj, string key, string def = "")
        {
            if (obj != null && obj.ContainsKey(key))
            {
                var v = obj.GetNamedValue(key);
                if (v.ValueType == JsonValueType.String) return v.GetString();
                if (v.ValueType == JsonValueType.Number) return v.GetNumber().ToString();
            }
            return def;
        }

        internal static int SafeGetInt(JsonObject obj, string key, int def = 0)
        {
            if (obj != null && obj.ContainsKey(key))
            {
                var v = obj.GetNamedValue(key);
                if (v.ValueType == JsonValueType.Number) return (int)v.GetNumber();
                if (v.ValueType == JsonValueType.String) { int n; if (int.TryParse(v.GetString(), out n)) return n; }
            }
            return def;
        }

        internal static long SafeGetLong(JsonObject obj, string key, long def = 0)
        {
            if (obj != null && obj.ContainsKey(key))
            {
                var v = obj.GetNamedValue(key);
                if (v.ValueType == JsonValueType.Number) return (long)v.GetNumber();
                if (v.ValueType == JsonValueType.String) { long n; if (long.TryParse(v.GetString(), out n)) return n; }
            }
            return def;
        }

        internal static double SafeGetDouble(JsonObject obj, string key, double def = 0)
        {
            if (obj != null && obj.ContainsKey(key))
            {
                var v = obj.GetNamedValue(key);
                if (v.ValueType == JsonValueType.Number) return v.GetNumber();
            }
            return def;
        }
    }
}
