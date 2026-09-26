using System;
using System.Collections.Generic;
using Windows.Data.Xml.Dom;
using Windows.Storage;
using Windows.UI.Notifications;
using Windows.UI.Xaml;
using 云湖WP.Api.Message;

namespace 云湖WP.Utils
{
    /// <summary>
    /// 系统通知帮助类 (支持群聊名称缓存、Toast 通知开关、前台防打扰、Live Tile 磁贴更新、Badge 数字角标及点击跳转解析)
    /// </summary>
    public static class NotificationHelper
    {
        private static readonly Dictionary<string, string> _chatTitles = new Dictionary<string, string>();
        private static readonly object _lock = new object();
        private static bool _cacheLoaded = false;
        private const string TITLES_CACHE_FILE = "chat_titles_cache.dat";
        private const string SETTINGS_KEY_CHAT_TITLES = "CachedChatTitlesMap";
        private const string SETTINGS_KEY_NOTIF_ENABLED = "Settings_NotificationEnabled";
        private const string SETTINGS_KEY_FOREGROUND_NOTIF = "Settings_ForegroundNotification";

        private static bool _isNotificationEnabled = true;
        private static bool _notifyInForeground = false; // 默认前台时不弹出系统横幅通知，避免前台干扰
        private static bool _isSavePending = false;

        /// <summary>
        /// 是否启用系统消息通知 (默认开启)
        /// </summary>
        public static bool IsNotificationEnabled
        {
            get { return _isNotificationEnabled; }
            set
            {
                if (_isNotificationEnabled != value)
                {
                    _isNotificationEnabled = value;
                    SaveSetting(SETTINGS_KEY_NOTIF_ENABLED, value);
                    if (!value)
                    {
                        ClearTile();
                        UpdateBadge(0);
                    }
                }
            }
        }

        /// <summary>
        /// 应用在前台运行时是否弹出横幅通知 (默认关闭)
        /// </summary>
        public static bool NotifyInForeground
        {
            get { return _notifyInForeground; }
            set
            {
                if (_notifyInForeground != value)
                {
                    _notifyInForeground = value;
                    SaveSetting(SETTINGS_KEY_FOREGROUND_NOTIF, value);
                }
            }
        }

        static NotificationHelper()
        {
            LoadChatTitlesCache();
            LoadNotificationSettings();
        }

        private static void LoadNotificationSettings()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values.ContainsKey(SETTINGS_KEY_NOTIF_ENABLED))
                {
                    object val = localSettings.Values[SETTINGS_KEY_NOTIF_ENABLED];
                    if (val is bool) _isNotificationEnabled = (bool)val;
                }
                if (localSettings.Values.ContainsKey(SETTINGS_KEY_FOREGROUND_NOTIF))
                {
                    object val = localSettings.Values[SETTINGS_KEY_FOREGROUND_NOTIF];
                    if (val is bool) _notifyInForeground = (bool)val;
                }
            }
            catch { }
        }

        private static void SaveSetting(string key, object value)
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values[key] = value;
            }
            catch { }
        }

        private static void LoadChatTitlesCache()
        {
            lock (_lock)
            {
                if (_cacheLoaded) return;
                _cacheLoaded = true;
            }

            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var folder = ApplicationData.Current.LocalFolder;
                    StorageFile file = null;
                    try
                    {
                        file = await folder.GetFileAsync(TITLES_CACHE_FILE);
                    }
                    catch { }

                    if (file != null)
                    {
                        string raw = await FileIO.ReadTextAsync(file);
                        if (!string.IsNullOrEmpty(raw))
                        {
                            var pairs = raw.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            lock (_lock)
                            {
                                foreach (var p in pairs)
                                {
                                    int tab = p.IndexOf('\t');
                                    if (tab > 0)
                                    {
                                        string id = p.Substring(0, tab).Trim();
                                        string title = p.Substring(tab + 1).Trim();
                                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(title) && !_chatTitles.ContainsKey(id))
                                        {
                                            _chatTitles[id] = title;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // 清理旧版存放在 LocalSettings 中的数据以释放空间
                    try
                    {
                        var localSettings = ApplicationData.Current.LocalSettings;
                        if (localSettings.Values.ContainsKey(SETTINGS_KEY_CHAT_TITLES))
                        {
                            localSettings.Values.Remove(SETTINGS_KEY_CHAT_TITLES);
                        }
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    AppLogger.Log("Notification", "LoadChatTitlesCache error: " + ex.Message);
                }
            });
        }

        private static void ScheduleSaveChatTitles()
        {
            lock (_lock)
            {
                if (_isSavePending) return;
                _isSavePending = true;
            }

            System.Threading.Tasks.Task.Run(async () =>
            {
                // 防抖延迟 500ms，让批量注册（例如加载几百个好友/群聊）合并为一次磁盘写入
                await System.Threading.Tasks.Task.Delay(500);

                string content;
                lock (_lock)
                {
                    _isSavePending = false;
                    var sb = new System.Text.StringBuilder();
                    foreach (var kv in _chatTitles)
                    {
                        if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                        {
                            sb.Append(kv.Key).Append('\t').Append(kv.Value.Replace('\n', ' ').Replace('\r', ' ')).Append('\n');
                        }
                    }
                    content = sb.ToString();
                }

                try
                {
                    var folder = ApplicationData.Current.LocalFolder;
                    var file = await folder.CreateFileAsync(TITLES_CACHE_FILE, CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteTextAsync(file, content);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("Notification", "SaveChatTitlesCache error: " + ex.Message);
                }
            });
        }

        /// <summary>
        /// 注册/更新会话或群聊名称到全局持久缓存
        /// </summary>
        public static void RegisterChatTitle(string chatId, string title)
        {
            if (string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(title)) return;
            chatId = chatId.Trim();
            title = title.Trim();

            // 过滤无效纯数字或占位标题
            if (title == chatId || title == "群聊消息" || title == "云湖好友" || title == "云湖会话") return;

            bool changed = false;
            lock (_lock)
            {
                if (!_chatTitles.ContainsKey(chatId) || _chatTitles[chatId] != title)
                {
                    _chatTitles[chatId] = title;
                    changed = true;
                }
            }

            if (changed)
            {
                ScheduleSaveChatTitles();
            }
        }

        /// <summary>
        /// 从全局缓存中获取会话或群聊名称
        /// </summary>
        public static string GetChatTitle(string chatId, int chatType = 1)
        {
            if (string.IsNullOrEmpty(chatId)) return chatType == 2 ? "群聊" : "云湖好友";

            chatId = chatId.Trim();
            lock (_lock)
            {
                if (_chatTitles.ContainsKey(chatId))
                {
                    return _chatTitles[chatId];
                }
            }

            return chatType == 2 ? "群聊 (" + chatId + ")" : (chatType == 3 ? "机器人" : "云湖好友");
        }

        private static volatile bool _isAppInForeground = true;

        /// <summary>
        /// 全局线程安全属性：当前应用是否在前台可见/活跃运行 (由 App 生命周期事件统一维护)
        /// </summary>
        public static bool IsAppInForeground
        {
            get { return _isAppInForeground; }
            set
            {
                _isAppInForeground = value;
                AppLogger.Log("Notification", string.Format("应用前台状态变更: IsAppInForeground={0}", value));
            }
        }

        /// <summary>
        /// 弹出系统 Toast 消息通知
        /// </summary>
        /// <param name="title">通知标题 (群聊名称或私聊发送者名称)</param>
        /// <param name="content">通知内容预览 (例如: 发送者: 消息正文)</param>
        /// <param name="chatId">会话 ID (用于点击后自动跳转)</param>
        /// <param name="chatType">会话类型 (1-私聊, 2-群聊, 3-机器人)</param>
        /// <param name="avatarUrl">可选头像链接</param>
        public static void ShowToast(string title, string content, string chatId = null, int chatType = 1, string avatarUrl = null)
        {
            if (!IsNotificationEnabled)
            {
                AppLogger.Log("Notification", "通知已全局关闭，跳过弹出 Toast");
                return;
            }

            // 如果应用处于前台，且设置了前台不弹出通知，则拦截跳过
            if (_isAppInForeground && !NotifyInForeground)
            {
                AppLogger.Log("Notification", string.Format("应用在前台运行中且已设置前台免打扰，静默拦截 Toast 通知: Title={0}", title));
                return;
            }

            try
            {
                if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(content)) return;

                // WP8.1 经典双行文本 Toast 模板
                var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
                var textNodes = toastXml.GetElementsByTagName("text");
                if (textNodes.Length >= 1)
                {
                    textNodes[0].AppendChild(toastXml.CreateTextNode(title ?? "云湖"));
                }
                if (textNodes.Length >= 2)
                {
                    textNodes[1].AppendChild(toastXml.CreateTextNode(content ?? ""));
                }

                // 注入点击跳转参数
                var toastElement = toastXml.SelectSingleNode("/toast") as XmlElement;
                if (toastElement != null && !string.IsNullOrEmpty(chatId))
                {
                    string launchArgs = string.Format("action=chat&chatId={0}&chatType={1}&title={2}",
                        Uri.EscapeDataString(chatId ?? ""),
                        chatType,
                        Uri.EscapeDataString(title ?? ""));
                    toastElement.SetAttribute("launch", launchArgs);
                }

                var toast = new ToastNotification(toastXml);
                // 1 小时后若未处理自动失效
                toast.ExpirationTime = DateTimeOffset.UtcNow.AddHours(1);

                ToastNotificationManager.CreateToastNotifier().Show(toast);
                AppLogger.Log("Notification", string.Format("已发送系统 Toast: Title={0}, Content={1}, ChatId={2}", title, content, chatId));
            }
            catch (Exception ex)
            {
                AppLogger.Log("Notification", "ShowToast 失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 根据 ChatMessageItem 自动提取群聊名称/发送者与内容摘要并弹出 Toast
        /// </summary>
        public static void ShowMessageToast(ChatMessageItem msg)
        {
            if (msg == null || !IsNotificationEnabled) return;

            string title = "新消息";
            string content = "";

            if (msg.ChatType == 2)
            {
                // 群聊消息：标题显示群聊名称，内容显示 [发言人: 消息内容]
                string groupName = GetChatTitle(msg.ChatId, 2);
                title = !string.IsNullOrEmpty(msg.ChatTitle) ? msg.ChatTitle : groupName;
                string sender = !string.IsNullOrEmpty(msg.SenderName) ? msg.SenderName : "群友";
                content = sender + ": " + GetMessageSummary(msg);
            }
            else
            {
                // 私聊或机器人消息：标题显示发送者姓名，内容显示消息内容
                string contactName = GetChatTitle(msg.ChatId, msg.ChatType);
                title = !string.IsNullOrEmpty(msg.SenderName) ? msg.SenderName : (!string.IsNullOrEmpty(msg.ChatTitle) ? msg.ChatTitle : contactName);
                content = GetMessageSummary(msg);
            }

            ShowToast(title, content, msg.ChatId, msg.ChatType, msg.SenderAvatarUrl);
        }

        /// <summary>
        /// 更新磁贴未读数字角标 (Badge)
        /// </summary>
        public static void UpdateBadge(int unreadCount)
        {
            if (!IsNotificationEnabled && unreadCount > 0) return;

            try
            {
                var updater = BadgeUpdateManager.CreateBadgeUpdaterForApplication();
                if (unreadCount > 0)
                {
                    var badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeNumber);
                    var badgeElement = badgeXml.SelectSingleNode("/badge") as XmlElement;
                    if (badgeElement != null)
                    {
                        badgeElement.SetAttribute("value", unreadCount > 99 ? "99+" : unreadCount.ToString());
                    }
                    updater.Update(new BadgeNotification(badgeXml));
                }
                else
                {
                    updater.Clear();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Notification", "UpdateBadge 异常: " + ex.Message);
            }
        }

        /// <summary>
        /// 更新 Live Tile 动态磁贴 (同时更新中磁贴与宽磁贴)
        /// </summary>
        public static void UpdateLiveTile(string title, string content)
        {
            if (!IsNotificationEnabled) return;

            try
            {
                if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(content)) return;

                // 宽磁贴模板 (TileWide310x150Text03)
                var wideXml = TileUpdateManager.GetTemplateContent(TileTemplateType.TileWide310x150Text03);
                var wideTexts = wideXml.GetElementsByTagName("text");
                if (wideTexts.Length >= 1) wideTexts[0].AppendChild(wideXml.CreateTextNode(title ?? "云湖"));
                if (wideTexts.Length >= 2) wideTexts[1].AppendChild(wideXml.CreateTextNode(content ?? ""));

                // 中磁贴模板 (TileSquare150x150Text04)
                var squareXml = TileUpdateManager.GetTemplateContent(TileTemplateType.TileSquare150x150Text04);
                var squareTexts = squareXml.GetElementsByTagName("text");
                if (squareTexts.Length >= 1) squareTexts[0].AppendChild(squareXml.CreateTextNode(title ?? "云湖"));

                // 合并中磁贴 binding 到 wideXml
                var squareBinding = squareXml.GetElementsByTagName("binding")[0];
                var importedBinding = wideXml.ImportNode(squareBinding, true);
                wideXml.GetElementsByTagName("visual")[0].AppendChild(importedBinding);

                var tileNotification = new TileNotification(wideXml);
                tileNotification.ExpirationTime = DateTimeOffset.UtcNow.AddDays(1);
                TileUpdateManager.CreateTileUpdaterForApplication().Update(tileNotification);
            }
            catch (Exception ex)
            {
                AppLogger.Log("Notification", "UpdateLiveTile 异常: " + ex.Message);
            }
        }

        /// <summary>
        /// 清除磁贴所有动态更新
        /// </summary>
        public static void ClearTile()
        {
            try
            {
                TileUpdateManager.CreateTileUpdaterForApplication().Clear();
            }
            catch { }
        }

        /// <summary>
        /// 解析 Toast 通知中的 launch 参数为 ChatNavigationArgs
        /// </summary>
        public static ChatNavigationArgs ParseChatLaunchArgs(string launchArgs)
        {
            if (string.IsNullOrEmpty(launchArgs)) return null;

            try
            {
                var queryParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var pairs = launchArgs.Split(new char[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var pair in pairs)
                {
                    int eqIndex = pair.IndexOf('=');
                    if (eqIndex > 0)
                    {
                        string k = pair.Substring(0, eqIndex);
                        string v = pair.Substring(eqIndex + 1);
                        queryParams[k] = Uri.UnescapeDataString(v);
                    }
                }

                if (queryParams.ContainsKey("chatId"))
                {
                    string chatId = queryParams["chatId"];
                    int chatType = 1;
                    if (queryParams.ContainsKey("chatType"))
                    {
                        int.TryParse(queryParams["chatType"], out chatType);
                    }
                    string title = queryParams.ContainsKey("title") ? queryParams["title"] : "";

                    // 若 title 为占位符，尝试从缓存中补齐
                    if (string.IsNullOrEmpty(title) || title == "群聊消息" || title == "云湖好友")
                    {
                        title = GetChatTitle(chatId, chatType);
                    }

                    return new ChatNavigationArgs
                    {
                        ChatId = chatId,
                        ChatType = chatType,
                        Title = title
                    };
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Notification", "ParseChatLaunchArgs 异常: " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// 格式化消息摘要
        /// </summary>
        public static string GetMessageSummary(ChatMessageItem msg)
        {
            if (msg == null) return "";
            if (msg.IsPostMsg)
            {
                return "[动态] " + (!string.IsNullOrEmpty(msg.DisplayPostTitle) ? msg.DisplayPostTitle : "分享");
            }

            switch (msg.ContentType)
            {
                case 2: return "[图片]";
                case 4: return "[文件] " + (!string.IsNullOrEmpty(msg.DisplayFileName) ? msg.DisplayFileName : "");
                case 5:
                case 10: return "[视频]";
                case 6:
                case 12:
                case 13: return "[动态] " + (!string.IsNullOrEmpty(msg.DisplayPostTitle) ? msg.DisplayPostTitle : "分享");
                case 7: return "[表情]";
                case 8: return "[网页/富文本]";
                case 11: return "[语音]";
                default:
                    if (!string.IsNullOrEmpty(msg.Text)) return msg.Text;
                    if (!string.IsNullOrEmpty(msg.ImageUrl)) return "[图片]";
                    if (!string.IsNullOrEmpty(msg.VideoUrl)) return "[视频]";
                    if (!string.IsNullOrEmpty(msg.FileUrl)) return "[文件]";
                    return "[消息]";
            }
        }
    }
}
