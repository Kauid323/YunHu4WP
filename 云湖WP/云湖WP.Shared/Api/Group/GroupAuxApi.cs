using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Group
{
    /// <summary>群聊相关的背景、分享、网盘、举报入口，按功能集中在 Group 目录。</summary>
    public static class GroupAuxApi
    {
        public static Task<string> SetChatBackgroundAsync(string token, string groupId, string url) { return Post(token, "/v1/chat-background/edit", new Dictionary<string, object> { { "chatId", groupId }, { "url", url } }); }
        public static Task<string> GetChatBackgroundsAsync(string token) { return Post(token, "/v1/chat-background/list", new Dictionary<string, object>()); }
        public static Task<string> CreateShareAsync(string token, string groupId, string name) { return Post(token, "/v1/share/create", new Dictionary<string, object> { { "chatId", groupId }, { "chatType", 2 }, { "chatName", name } }); }
        public static Task<string> ReportAsync(string token, string groupId, string name, string reason, string content) { return Post(token, "/v1/report/create", new Dictionary<string, object> { { "chatId", groupId }, { "chatType", 2 }, { "chatName", name }, { "reason", reason }, { "content", content }, { "url", "" } }); }
        public static Task<string> DiskListAsync(string token, string groupId, long folderId = 0) { return Post(token, "/v1/disk/file-list", new Dictionary<string, object> { { "chatId", groupId }, { "chatType", 2 }, { "folderId", folderId }, { "sort", "name_asc" } }); }
        private static Task<string> Post(string token, string path, Dictionary<string, object> values) { var json = new JsonObject(); foreach (var value in values) { if (value.Value is string) json.SetNamedValue(value.Key, JsonValue.CreateStringValue((string)value.Value)); else json.SetNamedValue(value.Key, JsonValue.CreateNumberValue(System.Convert.ToDouble(value.Value))); } return HttpHelper.PostJsonAsync(path, json.Stringify(), token); }
    }
}
