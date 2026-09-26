using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using 云湖WP.Api.Message;
using 云湖WP.Utils;

namespace 云湖WP.Api.Conversation
{
    /// <summary>
    /// 会话业务专用的 Diff 适配器，底层完全依托通用 Utils.DiffUtil
    /// </summary>
    public static class ConversationDiffUtil
    {
        /// <summary>
        /// 将全新的会话列表增量同步至现有的 ObservableCollection
        /// </summary>
        public static void ApplyDiff(ObservableCollection<ConversationItem> target, IList<ConversationItem> newList)
        {
            if (newList != null)
            {
                foreach (var item in newList)
                {
                    if (item != null && !string.IsNullOrEmpty(item.ChatId))
                    {
                        NotificationHelper.RegisterChatTitle(item.ChatId, item.DisplayTitle);
                    }
                }
            }

            DiffUtil.ApplyDiff(
                target,
                newList,
                item => item.ChatId + "_" + item.ChatType,
                (oldItem, newItem) => oldItem.UpdateFrom(newItem)
            );
        }

        /// <summary>
        /// 收到实时推送消息时，通过 DiffUtil 机制精准更新单个会话并置顶到列表首位
        /// </summary>
        public static void ApplyPushMessage(ObservableCollection<ConversationItem> target, ChatMessageItem msg, string activeChatId = "")
        {
            if (target == null || msg == null || string.IsNullOrEmpty(msg.ChatId)) return;

            string preview = GetMessageSummary(msg);
            string senderName = !string.IsNullOrEmpty(msg.SenderName) ? msg.SenderName : "";
            string chatContent;
            if (preview != null && preview.StartsWith("该消息已于"))
            {
                chatContent = preview;
            }
            else if (!string.IsNullOrEmpty(senderName))
            {
                chatContent = senderName + ":" + preview;
            }
            else
            {
                chatContent = preview;
            }

            long timeMs = msg.SendTime > 0 ? msg.SendTime : (DateTime.UtcNow.Ticks / 10000 - 62135596800000L);
            bool isCurrentChat = !string.IsNullOrEmpty(activeChatId) && activeChatId == msg.ChatId;

            // 获取该会话已知的群名/联系人名称
            string chatTitle = NotificationHelper.GetChatTitle(msg.ChatId, msg.ChatType);
            if (string.IsNullOrEmpty(chatTitle) || chatTitle.StartsWith("群聊 (") || chatTitle == "云湖好友")
            {
                chatTitle = !string.IsNullOrEmpty(msg.SenderName) ? msg.SenderName : msg.ChatId;
            }

            var newConv = new ConversationItem
            {
                ChatId = msg.ChatId,
                ChatType = msg.ChatType,
                Name = chatTitle,
                AvatarUrl = msg.SenderAvatarUrl ?? "",
                ChatContent = chatContent,
                TimestampMs = timeMs,
                UnreadCount = (!isCurrentChat && !msg.IsSelf) ? 1 : 0
            };

            DiffUtil.UpsertAndMoveToTop(
                target,
                newConv,
                item => item.ChatId + "_" + item.ChatType,
                (existingItem, item) =>
                {
                    existingItem.ChatContent = chatContent;
                    existingItem.TimestampMs = timeMs;
                    if (!isCurrentChat && !msg.IsSelf)
                    {
                        existingItem.UnreadCount++;
                    }
                    if (!string.IsNullOrEmpty(existingItem.DisplayTitle))
                    {
                        NotificationHelper.RegisterChatTitle(existingItem.ChatId, existingItem.DisplayTitle);
                    }
                    existingItem.NotifyAllChanged();
                }
            );
        }

        /// <summary>
        /// 获取消息简要摘要文本 (对照 Java 端 WsMsgConverter.toPreviewText)
        /// </summary>
        private static string GetMessageSummary(ChatMessageItem msg)
        {
            if (msg == null) return "";
            switch (msg.ContentType)
            {
                case 1:
                    return !string.IsNullOrEmpty(msg.Text) ? msg.Text : "[文本]";
                case 2:
                    return "[图片]";
                case 3:
                    return !string.IsNullOrEmpty(msg.Text) ? msg.Text : "[Markdown]";
                case 4:
                    return "[文件]" + (!string.IsNullOrEmpty(msg.FileName) ? " " + msg.FileName : "");
                case 5:
                    return "[表单]";
                case 6:
                    return "[文章]";
                case 7:
                    return "[表情]";
                case 8:
                    return "[网页/富文本]";
                case 10:
                    return "[视频]";
                case 11:
                    return "[语音]";
                case 13:
                    return "[语音通话]";
                default:
                    if (!string.IsNullOrEmpty(msg.Text)) return msg.Text;
                    if (!string.IsNullOrEmpty(msg.VideoUrl)) return "[视频]";
                    if (!string.IsNullOrEmpty(msg.ImageUrl)) return "[图片]";
                    if (!string.IsNullOrEmpty(msg.FileUrl)) return "[文件]";
                    return "[消息]";
            }
        }
    }
}
