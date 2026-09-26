using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Community.EditBoard
{
    /// <summary>
    /// 编辑板块 API  POST /v1/community/ba/edit
    /// </summary>
    public static class EditBoardApi
    {
        public static async Task<EditBoardResult> EditBoardAsync(
            string token, int baId, string name, string avatar)
        {
            var result = new EditBoardResult();
            if (string.IsNullOrEmpty(token)) { result.Code = -1; result.Msg = "Token 为空"; return result; }
            try
            {
                var req = new JsonObject();
                req.SetNamedValue("baId", JsonValue.CreateNumberValue(baId));
                req.SetNamedValue("name", JsonValue.CreateStringValue(name ?? ""));
                req.SetNamedValue("avatar", JsonValue.CreateStringValue(avatar ?? ""));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/community/ba/edit", req.Stringify(), token);
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
