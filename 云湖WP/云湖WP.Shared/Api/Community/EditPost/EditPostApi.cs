using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Community.EditPost
{
    /// <summary>
    /// 动态编辑/删除/置顶 API
    /// </summary>
    public static class EditPostApi
    {
        // ────────────────────────────────────────────────
        // 编辑动态  POST /v1/community/posts/edit
        // ────────────────────────────────────────────────
        public static async Task<EditPostResult> EditPostAsync(
            string token, long postId, string title, string content, int contentType)
        {
            var result = new EditPostResult();
            if (string.IsNullOrEmpty(token)) { result.Code = -1; result.Msg = "Token 为空"; return result; }
            try
            {
                var req = new JsonObject();
                req.SetNamedValue("postId", JsonValue.CreateNumberValue(postId));
                req.SetNamedValue("title", JsonValue.CreateStringValue(title ?? ""));
                req.SetNamedValue("content", JsonValue.CreateStringValue(content ?? ""));
                req.SetNamedValue("contentType", JsonValue.CreateNumberValue(contentType));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/posts/edit", req.Stringify(), token);
                JsonObject root;
                if (!JsonObject.TryParse(jsonStr ?? "", out root)) { result.Code = -1; result.Msg = "解析失败"; return result; }

                result.Code = SafeGetInt(root, "code", -1);
                result.Msg = SafeGetString(root, "msg", "");
                if (result.IsSuccess && root.ContainsKey("data"))
                {
                    var data = root.GetNamedValue("data");
                    if (data.ValueType == JsonValueType.Object)
                    {
                        result.PostId = (long)data.GetObject().GetNamedValue("id").GetNumber();
                    }
                }
            }
            catch (Exception ex) { result.Code = -1; result.Msg = ex.Message; }
            return result;
        }

        // ────────────────────────────────────────────────
        // 删除动态  POST /v1/community/posts/delete
        // ────────────────────────────────────────────────
        public static async Task<DeletePostResult> DeletePostAsync(string token, long postId)
        {
            var result = new DeletePostResult();
            if (string.IsNullOrEmpty(token)) { result.Code = -1; result.Msg = "Token 为空"; return result; }
            try
            {
                var req = new JsonObject();
                req.SetNamedValue("postId", JsonValue.CreateNumberValue(postId));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/posts/delete", req.Stringify(), token);
                JsonObject root;
                if (!JsonObject.TryParse(jsonStr ?? "", out root)) { result.Code = -1; result.Msg = "解析失败"; return result; }

                result.Code = SafeGetInt(root, "code", -1);
                result.Msg = SafeGetString(root, "msg", "");
            }
            catch (Exception ex) { result.Code = -1; result.Msg = ex.Message; }
            return result;
        }

        // ────────────────────────────────────────────────
        // 置顶/取消置顶  POST /v1/community/posts/edit-sticky
        // (传入 postId 即可，若已置顶自动取消)
        // ────────────────────────────────────────────────
        public static async Task<EditStickyResult> ToggleStickyAsync(string token, long postId)
        {
            var result = new EditStickyResult();
            if (string.IsNullOrEmpty(token)) { result.Code = -1; result.Msg = "Token 为空"; return result; }
            try
            {
                var req = new JsonObject();
                req.SetNamedValue("postId", JsonValue.CreateNumberValue(postId));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/posts/edit-sticky", req.Stringify(), token);
                JsonObject root;
                if (!JsonObject.TryParse(jsonStr ?? "", out root)) { result.Code = -1; result.Msg = "解析失败"; return result; }

                result.Code = SafeGetInt(root, "code", -1);
                result.Msg = SafeGetString(root, "msg", "");
            }
            catch (Exception ex) { result.Code = -1; result.Msg = ex.Message; }
            return result;
        }

        private static string SafeGetString(JsonObject obj, string key, string def = "")
        {
            if (obj != null && obj.ContainsKey(key))
            {
                var v = obj.GetNamedValue(key);
                if (v.ValueType == JsonValueType.String) return v.GetString();
                if (v.ValueType == JsonValueType.Number) return v.GetNumber().ToString();
            }
            return def;
        }

        private static int SafeGetInt(JsonObject obj, string key, int def = 0)
        {
            if (obj != null && obj.ContainsKey(key))
            {
                var v = obj.GetNamedValue(key);
                if (v.ValueType == JsonValueType.Number) return (int)v.GetNumber();
                if (v.ValueType == JsonValueType.String) { int n; if (int.TryParse(v.GetString(), out n)) return n; }
            }
            return def;
        }
    }
}
