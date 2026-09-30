using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Group
{
    public static class GroupTagApi
    {
        public static async Task<List<string>> ListAsync(string token, string groupId, string keyword)
        {
            var result = new List<string>();
            var text = await Post(token, "/v1/group-tag/list", new Dictionary<string, object> { { "groupId", groupId }, { "page", 1 }, { "size", 50 }, { "tag", keyword ?? "" } });
            JsonObject root;
            if (JsonObject.TryParse(text, out root) && root.ContainsKey("data") && root.GetNamedValue("data").ValueType == JsonValueType.Object)
            {
                var data = root.GetNamedObject("data");
                if (data.ContainsKey("list") && data.GetNamedValue("list").ValueType == JsonValueType.Array)
                    foreach (var item in data.GetNamedArray("list")) if (item.ValueType == JsonValueType.Object)
                    {
                        var obj = item.GetObject();
                        var name = obj.ContainsKey("tag") ? obj.GetNamedString("tag") : "";
                        var desc = obj.ContainsKey("desc") ? obj.GetNamedString("desc") : "";
                        result.Add(string.IsNullOrEmpty(desc) ? name : name + "\n" + desc);
                    }
            }
            return result;
        }
        public static Task<string> CreateAsync(string token, string groupId, string tag) { return Post(token, "/v1/group-tag/create", new Dictionary<string, object> { { "groupId", groupId }, { "tag", tag }, { "color", "#0078D7" }, { "desc", "" }, { "sort", 0 } }); }
        public static Task<string> EditAsync(string token, long id, string groupId, string tag, string color, string desc, int sort) { return Post(token, "/v1/group-tag/edit", new Dictionary<string, object> { { "id", id }, { "groupId", groupId }, { "tag", tag }, { "color", color }, { "desc", desc }, { "sort", sort } }); }
        public static Task<string> DeleteAsync(string token, long id) { return Post(token, "/v1/group-tag/delete", new Dictionary<string, object> { { "id", id } }); }
        public static Task<string> RelateAsync(string token, string userId, long tagId) { return Post(token, "/v1/group-tag/relate", new Dictionary<string, object> { { "userId", userId }, { "tagGroupId", tagId } }); }
        private static Task<string> Post(string token, string path, Dictionary<string, object> values) { var json = new JsonObject(); foreach (var value in values) { if (value.Value is string) json.SetNamedValue(value.Key, JsonValue.CreateStringValue((string)value.Value)); else json.SetNamedValue(value.Key, JsonValue.CreateNumberValue(System.Convert.ToDouble(value.Value))); } return HttpHelper.PostJsonAsync(path, json.Stringify(), token); }
    }
}
