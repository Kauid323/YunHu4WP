using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;
using 云湖WP.Utils;

namespace 云湖WP.Api.Group
{
    public class GroupDiskFile
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public long FileSize { get; set; }
        public int ObjectType { get; set; } // 1-文件夹, 2-文件
        public long UploadTime { get; set; }
        public string UploadBy { get; set; }
        public string UploadByName { get; set; }
        public string QiniuKey { get; set; }

        public bool IsFolder
        {
            get { return ObjectType == 1 || FileSize <= 0; }
        }

        public string TypeText
        {
            get { return IsFolder ? "文件夹" : FormatSize(FileSize); }
        }

        public string SubtitleText
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(UploadByName)) parts.Add(UploadByName);
                if (UploadTime > 0) parts.Add(FormatDate(UploadTime));
                if (!IsFolder && FileSize > 0) parts.Add(FormatSize(FileSize));
                return string.Join(" · ", parts);
            }
        }

        public string IconText
        {
            get
            {
                if (IsFolder) return "\uE188"; // 文件夹
                string ext = System.IO.Path.GetExtension(Name ?? "").ToLowerInvariant();
                if (ext == ".jpg" || ext == ".png" || ext == ".gif" || ext == ".webp" || ext == ".jpeg" || ext == ".bmp") return "\uE114"; // 图片
                if (ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".mov" || ext == ".flv" || ext == ".3gp") return "\uE116"; // 视频
                if (ext == ".mp3" || ext == ".m4a" || ext == ".flac" || ext == ".wav" || ext == ".aac") return "\uE189"; // 音频
                if (ext == ".zip" || ext == ".rar" || ext == ".7z" || ext == ".tar" || ext == ".gz") return "\uE133"; // 压缩包
                if (ext == ".pdf" || ext == ".doc" || ext == ".docx" || ext == ".txt" || ext == ".xls" || ext == ".xlsx" || ext == ".ppt") return "\uE160"; // 文档
                return "\uE132"; // 通用文件
            }
        }

        public string DownloadUrl
        {
            get
            {
                if (string.IsNullOrEmpty(QiniuKey)) return "";
                if (QiniuKey.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || QiniuKey.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return QiniuKey;
                if (QiniuKey.StartsWith("//"))
                    return "https:" + QiniuKey;
                return "https://chat-file.jwznb.com/" + QiniuKey.TrimStart('/');
            }
        }

        public static string FormatSize(long size)
        {
            if (size < 1024) return size + " B";
            if (size < 1024 * 1024) return (size / 1024.0).ToString("0.0") + " KB";
            if (size < 1024 * 1024 * 1024) return (size / (1024.0 * 1024.0)).ToString("0.0") + " MB";
            return (size / (1024.0 * 1024.0 * 1024.0)).ToString("0.00") + " GB";
        }

        public static string FormatDate(long timestamp)
        {
            try
            {
                var dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(timestamp).ToLocalTime();
                return dt.ToString("yyyy/MM/dd HH:mm");
            }
            catch { return ""; }
        }
    }

    public static class GroupDiskApi
    {
        public static async Task<List<GroupDiskFile>> ListAsync(string token, string groupId, long folderId = 0)
        {
            var result = new List<GroupDiskFile>();
            try
            {
                var text = await Post(token, "/v1/disk/file-list", new Dictionary<string, object>
                {
                    { "chatId", groupId },
                    { "chatType", 2 },
                    { "folderId", folderId },
                    { "sort", "name_asc" }
                });

                JsonObject root;
                if (JsonObject.TryParse(text, out root) && root.ContainsKey("data") && root.GetNamedValue("data").ValueType == JsonValueType.Object)
                {
                    var data = root.GetNamedObject("data");
                    if (data.ContainsKey("list") && data.GetNamedValue("list").ValueType == JsonValueType.Array)
                    {
                        foreach (var item in data.GetNamedArray("list"))
                        {
                            if (item.ValueType == JsonValueType.Object)
                            {
                                var obj = item.GetObject();
                                result.Add(new GroupDiskFile
                                {
                                    Id = obj.ContainsKey("id") ? (long)obj.GetNamedNumber("id") : 0,
                                    Name = obj.ContainsKey("name") ? obj.GetNamedString("name") : "未命名文件",
                                    FileSize = obj.ContainsKey("fileSize") ? (long)obj.GetNamedNumber("fileSize") : 0,
                                    ObjectType = obj.ContainsKey("objectType") ? (int)obj.GetNamedNumber("objectType") : 2,
                                    UploadTime = obj.ContainsKey("uploadTime") ? (long)obj.GetNamedNumber("uploadTime") : 0,
                                    UploadBy = obj.ContainsKey("uploadBy") ? obj.GetNamedString("uploadBy") : "",
                                    UploadByName = obj.ContainsKey("uploadByName") ? obj.GetNamedString("uploadByName") : "",
                                    QiniuKey = obj.ContainsKey("qiniuKey") ? obj.GetNamedString("qiniuKey") : ""
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupDiskApi", "ListAsync 异常: " + ex.Message);
            }
            return result;
        }

        public static async Task<long> GetTotalSizeAsync(string token, string groupId)
        {
            try
            {
                var text = await Post(token, "/v1/disk/file-size", new Dictionary<string, object>
                {
                    { "chatId", groupId },
                    { "chatType", 2 }
                });

                JsonObject root;
                if (JsonObject.TryParse(text, out root) && root.ContainsKey("data") && root.GetNamedValue("data").ValueType == JsonValueType.Object)
                {
                    var data = root.GetNamedObject("data");
                    if (data.ContainsKey("totalSize"))
                    {
                        return (long)data.GetNamedNumber("totalSize");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupDiskApi", "GetTotalSizeAsync 异常: " + ex.Message);
            }
            return 0;
        }

        public static async Task<ApiResult> CreateFolderAsync(string token, string groupId, string folderName, long parentFolderId = 0)
        {
            var result = new ApiResult();
            try
            {
                string text = await Post(token, "/v1/disk/create-folder", new Dictionary<string, object>
                {
                    { "chatId", groupId },
                    { "chatType", 2 },
                    { "folderName", folderName ?? "" },
                    { "parentFolderId", parentFolderId }
                });

                JsonObject root;
                if (JsonObject.TryParse(text, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "创建文件夹失败: " + ex.Message;
            }
            return result;
        }

        public static async Task<ApiResult> RenameAsync(string token, long id, int objectType, string newName)
        {
            var result = new ApiResult();
            try
            {
                string text = await Post(token, "/v1/disk/rename", new Dictionary<string, object>
                {
                    { "id", id },
                    { "objectType", objectType },
                    { "name", newName ?? "" }
                });

                JsonObject root;
                if (JsonObject.TryParse(text, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "重命名失败: " + ex.Message;
            }
            return result;
        }

        public static async Task<ApiResult> RemoveAsync(string token, long id, int objectType)
        {
            var result = new ApiResult();
            try
            {
                string text = await Post(token, "/v1/disk/remove", new Dictionary<string, object>
                {
                    { "id", id },
                    { "objectType", objectType }
                });

                JsonObject root;
                if (JsonObject.TryParse(text, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "删除失败: " + ex.Message;
            }
            return result;
        }

        public static async Task<ApiResult> RecordUploadAsync(string token, string groupId, string fileName, long fileSize, string fileMd5, string fileEtag, string qiniuKey, long folderId = 0)
        {
            var result = new ApiResult();
            try
            {
                string text = await Post(token, "/v1/disk/upload-file", new Dictionary<string, object>
                {
                    { "chatId", groupId },
                    { "chatType", 2 },
                    { "fileSize", fileSize },
                    { "fileName", fileName ?? "" },
                    { "fileMd5", fileMd5 ?? "" },
                    { "fileEtag", fileEtag ?? "" },
                    { "qiniuKey", qiniuKey ?? "" },
                    { "folderId", folderId }
                });

                JsonObject root;
                if (JsonObject.TryParse(text, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "登记上传文件失败: " + ex.Message;
            }
            return result;
        }

        private static Task<string> Post(string token, string path, Dictionary<string, object> values)
        {
            var json = new JsonObject();
            foreach (var value in values)
            {
                if (value.Value is string)
                    json.SetNamedValue(value.Key, JsonValue.CreateStringValue((string)value.Value));
                else if (value.Value is long || value.Value is int || value.Value is double)
                    json.SetNamedValue(value.Key, JsonValue.CreateNumberValue(Convert.ToDouble(value.Value)));
                else if (value.Value is bool)
                    json.SetNamedValue(value.Key, JsonValue.CreateBooleanValue((bool)value.Value));
            }
            return HttpHelper.PostJsonAsync(path, json.Stringify(), token);
        }
    }
}

