using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using SilentOrbit.ProtocolBuffers;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Message
{
    /// <summary>
    /// 消息相关 API (/v1/msg 路由，支持 /v1/msg/list-message-by-seq 与 /v1/msg/send-message)
    /// </summary>
    public static class MessageApi
    {
        /// <summary>
        /// 按消息序列获取聊天记录 (POST /v1/msg/list-message-by-seq)
        /// </summary>
        /// <param name="token">用户身份认证 Token</param>
        /// <param name="chatId">会话对象 ID</param>
        /// <param name="chatType">会话类型: 1-用户，2-群聊，3-机器人</param>
        /// <param name="msgSeq">消息序列号，0 代表拉取最新消息</param>
        public static async Task<MessageListResult> GetMessageListBySeqAsync(string token, string chatId, int chatType, long msgSeq = 0)
        {
            var result = new MessageListResult();
            if (string.IsNullOrEmpty(token))
            {
                result.Code = -1;
                result.Msg = "Token 不能为空";
                return result;
            }
            if (string.IsNullOrEmpty(chatId))
            {
                result.Code = -1;
                result.Msg = "chatId 不能为空";
                return result;
            }

            try
            {
                byte[] reqBody = EncodeListBySeqRequest(chatId, chatType, msgSeq);
                byte[] respBytes = await HttpHelper.PostProtobufAsync("/v1/msg/list-message-by-seq", reqBody, token);

                if (respBytes == null || respBytes.Length == 0)
                {
                    result.Code = -1;
                    result.Msg = "服务器返回空响应";
                    return result;
                }

                result = DecodeListBySeqResponse(respBytes);
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取消息失败: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 按消息ID分页获取更早的历史消息 (POST /v1/msg/list-message)
        /// </summary>
        /// <param name="token">用户身份认证 Token</param>
        /// <param name="chatId">会话对象 ID</param>
        /// <param name="chatType">会话类型: 1-用户，2-群聊，3-机器人</param>
        /// <param name="msgId">起始消息 ID (获取该 ID 之前的历史消息)</param>
        /// <param name="count">拉取条数 (默认 30)</param>
        public static async Task<MessageListResult> GetMessageListAsync(string token, string chatId, int chatType, string msgId, int count = 30)
        {
            var result = new MessageListResult();
            if (string.IsNullOrEmpty(token))
            {
                result.Code = -1;
                result.Msg = "Token 不能为空";
                return result;
            }
            if (string.IsNullOrEmpty(chatId))
            {
                result.Code = -1;
                result.Msg = "chatId 不能为空";
                return result;
            }

            try
            {
                byte[] reqBody = EncodeListRequest(chatId, chatType, msgId, count);
                byte[] respBytes = await HttpHelper.PostProtobufAsync("/v1/msg/list-message", reqBody, token);

                if (respBytes == null || respBytes.Length == 0)
                {
                    result.Code = -1;
                    result.Msg = "服务器返回空响应";
                    return result;
                }

                result = DecodeListBySeqResponse(respBytes);
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取更早历史消息失败: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 发送单条文本消息 (POST /v1/msg/send-message)
        /// </summary>
        public static async Task<SendMessageResult> SendTextMessageAsync(string token, string chatId, int chatType, string text, string customMsgId = null)
        {
            var result = new SendMessageResult();
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(text))
            {
                result.Code = -1;
                result.Msg = "参数不能为空";
                return result;
            }

            try
            {
                string msgId = !string.IsNullOrEmpty(customMsgId) ? customMsgId : Guid.NewGuid().ToString("N");
                result.MsgId = msgId;

                byte[] reqBody = EncodeSendMsgRequest(msgId, chatId, chatType, text, 1, null);
                byte[] respBytes = await HttpHelper.PostProtobufAsync("/v1/msg/send-message", reqBody, token);

                if (respBytes == null || respBytes.Length == 0)
                {
                    result.Code = -1;
                    result.Msg = "发送未收到响应";
                    return result;
                }

                // 解码 StatusResponse
                DecodeStatusResponse(respBytes, result);
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "发送消息异常: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 发送单条图片/附件消息 (POST /v1/msg/send-message, ContentType = 2)
        /// </summary>
        public static async Task<SendMessageResult> SendImageMessageAsync(string token, string chatId, int chatType, string imageUrl, string text = null, string customMsgId = null)
        {
            var result = new SendMessageResult();
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(imageUrl))
            {
                result.Code = -1;
                result.Msg = "参数不能为空";
                return result;
            }

            try
            {
                string msgId = !string.IsNullOrEmpty(customMsgId) ? customMsgId : Guid.NewGuid().ToString("N");
                result.MsgId = msgId;

                string msgText = !string.IsNullOrEmpty(text) ? text : string.Format("![图片]({0})", imageUrl);
                byte[] reqBody = EncodeSendMsgRequest(msgId, chatId, chatType, msgText, 2, imageUrl);
                byte[] respBytes = await HttpHelper.PostProtobufAsync("/v1/msg/send-message", reqBody, token);

                if (respBytes == null || respBytes.Length == 0)
                {
                    result.Code = -1;
                    result.Msg = "发送未收到响应";
                    return result;
                }

                DecodeStatusResponse(respBytes, result);
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "发送图片异常: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 发送单条视频消息 (POST /v1/msg/send-message, ContentType = 10 视频)
        /// 严格遵循 API 规范：包含 Content (file_name, file_size, video) 与 Media (file_key, file_hash, file_type, image_width, image_height, file_size, file_key2, file_suffix)
        /// </summary>
        public static async Task<SendMessageResult> SendVideoMessageAsync(
            string token, 
            string chatId, 
            int chatType, 
            string fileKey, 
            string fileHash = null, 
            string fileName = null, 
            long fileSize = 0, 
            int width = 0, 
            int height = 0, 
            string mimeType = null, 
            string fileExtension = null, 
            string customMsgId = null)
        {
            var result = new SendMessageResult();
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(fileKey))
            {
                result.Code = -1;
                result.Msg = "参数不能为空";
                return result;
            }

            try
            {
                string msgId = !string.IsNullOrEmpty(customMsgId) ? customMsgId : Guid.NewGuid().ToString("N");
                result.MsgId = msgId;

                byte[] reqBody = EncodeSendVideoMsgRequest(msgId, chatId, chatType, fileKey, fileHash, fileName, fileSize, width, height, mimeType, fileExtension);
                byte[] respBytes = await HttpHelper.PostProtobufAsync("/v1/msg/send-message", reqBody, token);

                if (respBytes == null || respBytes.Length == 0)
                {
                    result.Code = -1;
                    result.Msg = "发送未收到响应";
                    return result;
                }

                DecodeStatusResponse(respBytes, result);
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "发送视频异常: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 撤回单条消息 (POST /v1/msg/recall-msg)
        /// </summary>
        public static async Task<ApiResult> RecallMessageAsync(string token, string msgId, string chatId, int chatType)
        {
            var result = new ApiResult();
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(msgId) || string.IsNullOrEmpty(chatId))
            {
                result.Code = -1;
                result.Msg = "参数不能为空";
                return result;
            }

            try
            {
                byte[] reqBody = EncodeRecallMsgRequest(msgId, chatId, chatType);
                byte[] respBytes = await HttpHelper.PostProtobufAsync("/v1/msg/recall-msg", reqBody, token);

                if (respBytes == null || respBytes.Length == 0)
                {
                    result.Code = -1;
                    result.Msg = "撤回未收到响应";
                    return result;
                }

                DecodeStatusResponse(respBytes, result);
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "撤回消息异常: " + ex.Message;
            }

            return result;
        }

        #region Protobuf 编码与解码逻辑

        /// <summary>
        /// 编码 list_message_send (用于 /v1/msg/list-message)
        /// </summary>
        private static byte[] EncodeListRequest(string chatId, int chatType, string msgId, int count = 30)
        {
            using (var ms = new MemoryStream())
            {
                if (count > 0)
                {
                    ProtocolParser.WriteKey(ms, new Key(2, Wire.Varint));
                    ProtocolParser.WriteUInt64(ms, (ulong)count);
                }

                if (!string.IsNullOrEmpty(msgId))
                {
                    ProtocolParser.WriteKey(ms, new Key(3, Wire.LengthDelimited));
                    ProtocolParser.WriteString(ms, msgId);
                }

                if (chatType != 0)
                {
                    ProtocolParser.WriteKey(ms, new Key(4, Wire.Varint));
                    ProtocolParser.WriteUInt64(ms, (ulong)chatType);
                }

                if (!string.IsNullOrEmpty(chatId))
                {
                    ProtocolParser.WriteKey(ms, new Key(5, Wire.LengthDelimited));
                    ProtocolParser.WriteString(ms, chatId);
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// 编码 list_message_by_seq_send
        /// </summary>
        private static byte[] EncodeListBySeqRequest(string chatId, int chatType, long msgSeq)
        {
            using (var ms = new MemoryStream())
            {
                if (msgSeq != 0)
                {
                    ProtocolParser.WriteKey(ms, new Key(3, Wire.Varint));
                    ProtocolParser.WriteUInt64(ms, (ulong)msgSeq);
                }

                if (chatType != 0)
                {
                    ProtocolParser.WriteKey(ms, new Key(4, Wire.Varint));
                    ProtocolParser.WriteUInt64(ms, (ulong)chatType);
                }

                if (!string.IsNullOrEmpty(chatId))
                {
                    ProtocolParser.WriteKey(ms, new Key(5, Wire.LengthDelimited));
                    ProtocolParser.WriteString(ms, chatId);
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// 编码 send_message_send
        /// </summary>
        private static byte[] EncodeSendMsgRequest(string msgId, string chatId, int chatType, string text, int contentType = 1, string imageUrl = null)
        {
            using (var ms = new MemoryStream())
            {
                if (!string.IsNullOrEmpty(msgId))
                {
                    ProtocolParser.WriteKey(ms, new Key(2, Wire.LengthDelimited));
                    ProtocolParser.WriteString(ms, msgId);
                }

                if (!string.IsNullOrEmpty(chatId))
                {
                    ProtocolParser.WriteKey(ms, new Key(3, Wire.LengthDelimited));
                    ProtocolParser.WriteString(ms, chatId);
                }

                if (chatType != 0)
                {
                    ProtocolParser.WriteKey(ms, new Key(4, Wire.Varint));
                    ProtocolParser.WriteUInt64(ms, (ulong)chatType);
                }

                // Field 5: Content
                using (var contentMs = new MemoryStream())
                {
                    if (!string.IsNullOrEmpty(text))
                    {
                        ProtocolParser.WriteKey(contentMs, new Key(1, Wire.LengthDelimited));
                        ProtocolParser.WriteString(contentMs, text);
                    }

                    if (!string.IsNullOrEmpty(imageUrl))
                    {
                        ProtocolParser.WriteKey(contentMs, new Key(3, Wire.LengthDelimited));
                        ProtocolParser.WriteString(contentMs, imageUrl);
                    }

                    byte[] contentBytes = contentMs.ToArray();
                    ProtocolParser.WriteKey(ms, new Key(5, Wire.LengthDelimited));
                    ProtocolParser.WriteBytes(ms, contentBytes);
                }

                if (contentType != 0)
                {
                    ProtocolParser.WriteKey(ms, new Key(6, Wire.Varint));
                    ProtocolParser.WriteUInt64(ms, (ulong)contentType);
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// 编码发送视频消息的 Protobuf 请求包 (send_message_send, ContentType = 10 视频)
        /// </summary>
        private static byte[] EncodeSendVideoMsgRequest(
            string msgId, 
            string chatId, 
            int chatType, 
            string fileKey, 
            string fileHash = null, 
            string fileName = null, 
            long fileSize = 0, 
            int width = 0, 
            int height = 0, 
            string mimeType = null, 
            string fileExtension = null)
        {
            if (string.IsNullOrEmpty(fileExtension))
            {
                if (!string.IsNullOrEmpty(fileKey) && fileKey.Contains("."))
                {
                    fileExtension = fileKey.Substring(fileKey.LastIndexOf('.') + 1).ToLowerInvariant();
                }
                else
                {
                    fileExtension = "mp4";
                }
            }

            if (string.IsNullOrEmpty(mimeType))
            {
                mimeType = "video/" + fileExtension;
            }

            if (string.IsNullOrEmpty(fileHash))
            {
                if (!string.IsNullOrEmpty(fileKey))
                {
                    int dot = fileKey.IndexOf('.');
                    fileHash = dot > 0 ? fileKey.Substring(0, dot) : fileKey;
                }
                else
                {
                    fileHash = "";
                }
            }

            using (var ms = new MemoryStream())
            {
                // Field 2: msg_id
                if (!string.IsNullOrEmpty(msgId))
                {
                    ProtocolParser.WriteKey(ms, new Key(2, Wire.LengthDelimited));
                    ProtocolParser.WriteString(ms, msgId);
                }

                // Field 3: chat_id
                if (!string.IsNullOrEmpty(chatId))
                {
                    ProtocolParser.WriteKey(ms, new Key(3, Wire.LengthDelimited));
                    ProtocolParser.WriteString(ms, chatId);
                }

                // Field 4: chat_type (int64 in proto)
                if (chatType != 0)
                {
                    ProtocolParser.WriteKey(ms, new Key(4, Wire.Varint));
                    ProtocolParser.WriteUInt64(ms, (ulong)chatType);
                }

                // Field 5: Content (send_message_send.Content)
                using (var contentMs = new MemoryStream())
                {
                    // Field 4: file_name
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        ProtocolParser.WriteKey(contentMs, new Key(4, Wire.LengthDelimited));
                        ProtocolParser.WriteString(contentMs, fileName);
                    }

                    // Field 18: file_size
                    if (fileSize > 0)
                    {
                        ProtocolParser.WriteKey(contentMs, new Key(18, Wire.Varint));
                        ProtocolParser.WriteUInt64(contentMs, (ulong)fileSize);
                    }

                    // Field 19: video (欲发送视频 key/url, 例如 "123.mp4")
                    if (!string.IsNullOrEmpty(fileKey))
                    {
                        ProtocolParser.WriteKey(contentMs, new Key(19, Wire.LengthDelimited));
                        ProtocolParser.WriteString(contentMs, fileKey);
                    }

                    byte[] contentBytes = contentMs.ToArray();
                    ProtocolParser.WriteKey(ms, new Key(5, Wire.LengthDelimited));
                    ProtocolParser.WriteBytes(ms, contentBytes);
                }

                // Field 6: content_type = 10 (视频)
                ProtocolParser.WriteKey(ms, new Key(6, Wire.Varint));
                ProtocolParser.WriteUInt64(ms, 10);

                // Field 9: Media
                using (var mediaMs = new MemoryStream())
                {
                    // Field 1: file_key
                    if (!string.IsNullOrEmpty(fileKey))
                    {
                        ProtocolParser.WriteKey(mediaMs, new Key(1, Wire.LengthDelimited));
                        ProtocolParser.WriteString(mediaMs, fileKey);
                    }

                    // Field 2: file_hash
                    if (!string.IsNullOrEmpty(fileHash))
                    {
                        ProtocolParser.WriteKey(mediaMs, new Key(2, Wire.LengthDelimited));
                        ProtocolParser.WriteString(mediaMs, fileHash);
                    }

                    // Field 3: file_type
                    ProtocolParser.WriteKey(mediaMs, new Key(3, Wire.LengthDelimited));
                    ProtocolParser.WriteString(mediaMs, mimeType);

                    // Field 5: image_height
                    if (height > 0)
                    {
                        ProtocolParser.WriteKey(mediaMs, new Key(5, Wire.Varint));
                        ProtocolParser.WriteUInt64(mediaMs, (ulong)height);
                    }

                    // Field 6: image_width
                    if (width > 0)
                    {
                        ProtocolParser.WriteKey(mediaMs, new Key(6, Wire.Varint));
                        ProtocolParser.WriteUInt64(mediaMs, (ulong)width);
                    }

                    // Field 7: file_size
                    if (fileSize > 0)
                    {
                        ProtocolParser.WriteKey(mediaMs, new Key(7, Wire.Varint));
                        ProtocolParser.WriteUInt64(mediaMs, (ulong)fileSize);
                    }

                    // Field 8: file_key2 (和 file_key 一样)
                    if (!string.IsNullOrEmpty(fileKey))
                    {
                        ProtocolParser.WriteKey(mediaMs, new Key(8, Wire.LengthDelimited));
                        ProtocolParser.WriteString(mediaMs, fileKey);
                    }

                    // Field 9: file_suffix
                    if (!string.IsNullOrEmpty(fileExtension))
                    {
                        ProtocolParser.WriteKey(mediaMs, new Key(9, Wire.LengthDelimited));
                        ProtocolParser.WriteString(mediaMs, fileExtension);
                    }

                    byte[] mediaBytes = mediaMs.ToArray();
                    ProtocolParser.WriteKey(ms, new Key(9, Wire.LengthDelimited));
                    ProtocolParser.WriteBytes(ms, mediaBytes);
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// 编码 recall_msg_send
        /// </summary>
        private static byte[] EncodeRecallMsgRequest(string msgId, string chatId, int chatType)
        {
            using (var ms = new MemoryStream())
            {
                if (!string.IsNullOrEmpty(msgId))
                {
                    ProtocolParser.WriteKey(ms, new Key(2, Wire.LengthDelimited));
                    ProtocolParser.WriteString(ms, msgId);
                }

                if (!string.IsNullOrEmpty(chatId))
                {
                    ProtocolParser.WriteKey(ms, new Key(3, Wire.LengthDelimited));
                    ProtocolParser.WriteString(ms, chatId);
                }

                if (chatType != 0)
                {
                    ProtocolParser.WriteKey(ms, new Key(4, Wire.Varint));
                    ProtocolParser.WriteUInt64(ms, (ulong)chatType);
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// 解码 list_message_by_seq 与 list-message 响应体 (直接使用官方生成的 ListMsgResponse.Deserialize 反序列化)
        /// </summary>
        public static MessageListResult DecodeListBySeqResponse(byte[] bytes)
        {
            var result = new MessageListResult();
            if (bytes == null || bytes.Length == 0) return result;

            // 针对 JSON 错误降级处理
            if (bytes[0] == '{')
            {
                try
                {
                    string jsonStr = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                    JsonObject json;
                    if (JsonObject.TryParse(jsonStr, out json))
                    {
                        if (json.ContainsKey("code")) result.Code = (int)json.GetNamedNumber("code");
                        if (json.ContainsKey("msg")) result.Msg = json.GetNamedString("msg");
                        return result;
                    }
                }
                catch { }
            }

            try
            {
                var resp = global::Yh.ListMsgResponse.Deserialize(bytes);
                if (resp.Status != null)
                {
                    result.Code = resp.Status.Code;
                    result.Msg = resp.Status.Msg ?? "";
                }
                else
                {
                    result.Code = 0;
                }

                result.Total = resp.Total;

                if (resp.Data != null)
                {
                    foreach (var d in resp.Data)
                    {
                        if (d == null) continue;
                        var item = new ChatMessageItem();
                        item.MsgId = d.MsgId ?? "";
                        item.Direction = d.Direction ?? "left";
                        item.ContentType = (int)d.ContentType;
                        item.SendTime = (long)d.SendTimestampMs;
                        item.MsgSeq = (long)d.MsgSeq;
                        item.QuoteMsgId = d.QuoteMsgId ?? "";

                        if (d.Sender != null)
                        {
                            item.SenderId = d.Sender.ChatId ?? "";
                            item.SenderName = d.Sender.Name ?? "";
                            item.SenderAvatarUrl = d.Sender.AvatarUrl ?? "";
                        }

                        if (d.Content != null)
                        {
                            item.Text = d.Content.Text ?? "";
                            item.ImageUrl = d.Content.ImageUrl ?? (d.Content.Image ?? "");
                            item.FileName = d.Content.FileName ?? "";
                            item.FileUrl = d.Content.FileUrl ?? "";
                            item.FileSize = (long)d.Content.FileSize;
                            item.QuoteMsgText = d.Content.QuoteMsgText ?? "";
                            item.StickerUrl = d.Content.ExpressionId ?? "";
                            item.VideoUrl = d.Content.VideoUrl ?? "";
                            item.VideoDuration = d.Content.VideoTime;
                            item.QuoteVideoUrl = d.Content.QuoteVideoUrl ?? "";
                            item.QuoteVideoDuration = d.Content.QuoteVideoTime;
                            item.MediaWidth = d.Content.Width;
                            item.MediaHeight = d.Content.Height;
                            item.Tip = d.Content.Tip ?? "";
                        }

                        item.InitParsedData();
                        result.Messages.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "消息 Protobuf 解析异常: " + ex.Message;
            }

            return result;
        }

        private static void DecodeStatusResponse(byte[] bytes, ApiResult target)
        {
            if (bytes == null || bytes.Length == 0) return;
            try
            {
                var resp = global::Yh.StatusResponse.Deserialize(bytes);
                if (resp.Status != null)
                {
                    target.Code = resp.Status.Code;
                    target.Msg = resp.Status.Msg ?? "";
                }
                else
                {
                    target.Code = 0;
                }
            }
            catch
            {
                target.Code = 0;
            }
        }

        #endregion
    }
}
