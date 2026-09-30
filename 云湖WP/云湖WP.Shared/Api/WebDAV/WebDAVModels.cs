using System;
using System.Collections.Generic;

namespace 云湖WP.Api.WebDAV
{
    /// <summary>
    /// 群 WebDAV 挂载配置项
    /// </summary>
    public class WebDAVMountSetting
    {
        public long Id { get; set; }
        public string GroupId { get; set; }
        public string MountName { get; set; }
        public string WebdavUrl { get; set; }
        public string WebdavUserName { get; set; }
        public string WebdavPassword { get; set; } // 解密后的明文密码
        public string EncryptedPassword { get; set; } // 原始密文
        public string WebdavRootPath { get; set; }
        public long CreateTime { get; set; }
        public string UserId { get; set; }

        public string DisplayTitle
        {
            get { return !string.IsNullOrEmpty(MountName) ? MountName : "未命名挂载点"; }
        }

        public string DisplaySubtitle
        {
            get
            {
                string u = !string.IsNullOrEmpty(WebdavUrl) ? WebdavUrl : "";
                string user = !string.IsNullOrEmpty(WebdavUserName) ? (" (" + WebdavUserName + ")") : "";
                return u + user;
            }
        }
    }

    /// <summary>
    /// WebDAV 文件或目录项
    /// </summary>
    public class WebDAVFile
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime? LastModified { get; set; }
        public WebDAVMountSetting MountSetting { get; set; }

        public string TypeText
        {
            get { return IsDirectory ? "文件夹" : FormatSize(Size); }
        }

        public string IconText
        {
            get
            {
                if (IsDirectory) return "\uE188";
                string ext = System.IO.Path.GetExtension(Name ?? "").ToLowerInvariant();
                if (ext == ".jpg" || ext == ".png" || ext == ".gif" || ext == ".webp" || ext == ".jpeg") return "\uE114";
                if (ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".mov") return "\uE116";
                if (ext == ".mp3" || ext == ".m4a" || ext == ".flac" || ext == ".wav") return "\uE189";
                if (ext == ".zip" || ext == ".rar" || ext == ".7z" || ext == ".tar") return "\uE133";
                if (ext == ".pdf" || ext == ".doc" || ext == ".docx" || ext == ".txt") return "\uE160";
                return "\uE132";
            }
        }

        public string SubtitleText
        {
            get
            {
                var list = new List<string>();
                if (IsDirectory) list.Add("目录");
                else list.Add(FormatSize(Size));
                if (LastModified.HasValue) list.Add(LastModified.Value.ToString("yyyy/MM/dd HH:mm"));
                return string.Join(" · ", list);
            }
        }

        public static string FormatSize(long size)
        {
            if (size < 1024) return size + " B";
            if (size < 1024 * 1024) return (size / 1024.0).ToString("0.0") + " KB";
            if (size < 1024 * 1024 * 1024) return (size / (1024.0 * 1024.0)).ToString("0.0") + " MB";
            return (size / (1024.0 * 1024.0 * 1024.0)).ToString("0.00") + " GB";
        }
    }
}
