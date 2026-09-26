using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.UI.Popups;
using Windows.UI.Xaml.Controls;
using 云湖WP.Api.Friend;
using 云湖WP.Api.Message;
using 云湖WP.Api.Share;
using 云湖WP.Token;

namespace 云湖WP.Utils
{
    /// <summary>
    /// 分享链接解析与交互调度器
    /// </summary>
    public static class ShareHelper
    {
        private static readonly Regex KeyRegex = new Regex(@"(?:[?&]key=)([^&\s]+)", RegexOptions.IgnoreCase);
        private static readonly Regex TsRegex = new Regex(@"(?:[?&]ts=)([^&\s]+)", RegexOptions.IgnoreCase);

        /// <summary>
        /// 从链接或字符串中提取 key 与 ts 参数
        /// </summary>
        public static bool ExtractKeyAndTs(string urlOrKey, out string key, out string ts)
        {
            key = "";
            ts = "";

            if (string.IsNullOrWhiteSpace(urlOrKey)) return false;

            var matchKey = KeyRegex.Match(urlOrKey);
            if (matchKey.Success)
            {
                key = matchKey.Groups[1].Value;
            }
            else
            {
                // 如果没有 query key=，且不含斜杠等符号，可能直接传的就是 key
                if (!urlOrKey.Contains("/") && !urlOrKey.Contains("?"))
                {
                    key = urlOrKey.Trim();
                }
            }

            var matchTs = TsRegex.Match(urlOrKey);
            if (matchTs.Success)
            {
                ts = matchTs.Groups[1].Value;
            }

            return !string.IsNullOrEmpty(key);
        }

        /// <summary>
        /// 处理分享链接：解析详情、弹出交互对话框、支持申请添加或直接跳转聊天
        /// </summary>
        public static async Task HandleShareLinkAsync(Frame frame, string rawUrlOrKey, string fallbackChatId = "", string fallbackChatName = "", int fallbackChatType = 2)
        {
            string token = await TokenManager.GetTokenAsync();
            string key, ts;
            ExtractKeyAndTs(rawUrlOrKey, out key, out ts);

            string chatId = fallbackChatId;
            string chatName = fallbackChatName;
            int chatType = fallbackChatType;
            string createBy = "";
            long createTime = 0;
            string avatarUrl = "";

            if (!string.IsNullOrEmpty(key))
            {
                var shareResult = await ShareApi.GetShareInfoAsync(token, key, ts);
                if (shareResult != null && shareResult.Code == 1 && shareResult.Share != null)
                {
                    var item = shareResult.Share;
                    if (!string.IsNullOrEmpty(item.ChatId)) chatId = item.ChatId;
                    if (!string.IsNullOrEmpty(item.ChatName)) chatName = item.ChatName;
                    if (item.ChatType > 0) chatType = item.ChatType;
                    createBy = item.CreateBy;
                    createTime = item.CreateTime;
                    avatarUrl = item.ImageUrl;
                }
            }

            if (string.IsNullOrEmpty(chatId))
            {
                var errDialog = new MessageDialog("无法解析分享链接或未找到对应会话信息。", "分享链接解析");
                await errDialog.ShowAsync();
                return;
            }

            string typeText = (chatType == 2) ? "群聊" : ((chatType == 3) ? "机器人" : "私聊");
            string dialogContent = string.Format(
                "会话名称: {0}\n会话 ID: {1}\n会话类型: {2}{3}{4}",
                string.IsNullOrEmpty(chatName) ? "未知" : chatName,
                chatId,
                typeText,
                !string.IsNullOrEmpty(createBy) ? string.Format("\n创建者: {0}", createBy) : "",
                !string.IsNullOrEmpty(key) ? string.Format("\n分享 Key: {0}", key) : ""
            );

            // Windows Phone 8.1 限制：MessageDialog 最多支持 2 个 UICommand，超过 2 个会抛出 ArgumentException
            var dialog = new MessageDialog(dialogContent, "分享链接详情");

            dialog.Commands.Add(new UICommand("申请加入", async (cmd) =>
            {
                var applyResult = await FriendApi.AddFriendAsync(token, chatId, chatType, "通过分享链接申请加入");
                if (applyResult != null && applyResult.Code == 1)
                {
                    var tip = new MessageDialog("申请加入已提交成功！是否立即进入该会话？", "云湖");
                    tip.Commands.Add(new UICommand("进入会话", (c) =>
                    {
                        NavigateToChat(frame, chatId, chatType, chatName, avatarUrl, token);
                    }));
                    tip.Commands.Add(new UICommand("关闭"));
                    tip.DefaultCommandIndex = 0;
                    tip.CancelCommandIndex = 1;
                    await tip.ShowAsync();
                }
                else
                {
                    string err = (applyResult != null && !string.IsNullOrEmpty(applyResult.Msg)) ? applyResult.Msg : "请求失败或已在该群聊中";
                    var tip = new MessageDialog("申请加入提示: " + err + "\n是否尝试直接进入聊天？", "云湖");
                    tip.Commands.Add(new UICommand("进入聊天", (c) =>
                    {
                        NavigateToChat(frame, chatId, chatType, chatName, avatarUrl, token);
                    }));
                    tip.Commands.Add(new UICommand("关闭"));
                    tip.DefaultCommandIndex = 0;
                    tip.CancelCommandIndex = 1;
                    await tip.ShowAsync();
                }
            }));

            dialog.Commands.Add(new UICommand("取消"));

            dialog.DefaultCommandIndex = 0;
            dialog.CancelCommandIndex = 1;

            await dialog.ShowAsync();
        }

        private static void NavigateToChat(Frame frame, string chatId, int chatType, string chatName, string avatarUrl, string token)
        {
            if (frame == null) return;

            var args = new ChatNavigationArgs
            {
                ChatId = chatId,
                ChatType = chatType,
                Title = chatName,
                AvatarUrl = avatarUrl,
                Token = token
            };

#if WINDOWS_PHONE_APP
            frame.Navigate(typeof(ChatPage), args);
#else
            var chatPageType = Type.GetType("云湖WP.ChatPage");
            if (chatPageType != null)
            {
                frame.Navigate(chatPageType, args);
            }
#endif
        }
    }
}
