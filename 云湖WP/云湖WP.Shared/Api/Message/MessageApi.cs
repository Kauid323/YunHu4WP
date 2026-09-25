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
        public static async Task<SendMessageResult> SendTextMessageAsync(string token, string chatId, int chatType, string text)
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
                string msgId = Guid.NewGuid().ToString("N");
                result.MsgId = msgId;

                byte[] reqBody = EncodeSendMsgRequest(msgId, chatId, chatType, text, 1);
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
        private static byte[] EncodeSendMsgRequest(string msgId, string chatId, int chatType, string text, int contentType = 1)
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
        /// 解码 list_message_by_seq 响应体
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
                using (var stream = new MemoryStream(bytes))
                {
                    while (stream.Position < stream.Length)
                    {
                        var key = ProtocolParser.ReadKey(stream);
                        if (key.Field == 0) break;

                        switch (key.Field)
                        {
                            case 1: // Status
                                using (var statusStream = ReadSubStream(stream))
                                {
                                    DecodeStatus(statusStream, result);
                                }
                                break;
                            case 2: // Msg (repeated)
                                using (var msgStream = ReadSubStream(stream))
                                {
                                    var item = DecodeMsg(msgStream);
                                    if (item != null)
                                    {
                                        result.Messages.Add(item);
                                    }
                                }
                                break;
                            case 3: // total
                                result.Total = (int)ProtocolParser.ReadUInt32(stream);
                                break;
                            default:
                                ProtocolParser.SkipKey(stream, key);
                                break;
                        }
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

        private static void DecodeStatus(Stream stream, ApiResult target)
        {
            while (stream.Position < stream.Length)
            {
                var key = ProtocolParser.ReadKey(stream);
                if (key.Field == 0) break;

                switch (key.Field)
                {
                    case 2:
                        target.Code = (int)ProtocolParser.ReadUInt32(stream);
                        break;
                    case 3:
                        target.Msg = ProtocolParser.ReadString(stream);
                        break;
                    default:
                        ProtocolParser.SkipKey(stream, key);
                        break;
                }
            }
        }

        private static ChatMessageItem DecodeMsg(Stream stream)
        {
            var item = new ChatMessageItem();

            while (stream.Position < stream.Length)
            {
                var key = ProtocolParser.ReadKey(stream);
                if (key.Field == 0) break;

                switch (key.Field)
                {
                    case 1: // msg_id
                        item.MsgId = ProtocolParser.ReadString(stream);
                        break;
                    case 2: // Sender
                        using (var senderStream = ReadSubStream(stream))
                        {
                            DecodeSender(senderStream, item);
                        }
                        break;
                    case 3: // direction ("left" / "right")
                        item.Direction = ProtocolParser.ReadString(stream);
                        break;
                    case 4: // content_type
                        item.ContentType = (int)ProtocolParser.ReadUInt32(stream);
                        break;
                    case 5: // Content
                        using (var contentStream = ReadSubStream(stream))
                        {
                            DecodeContent(contentStream, item);
                        }
                        break;
                    case 6: // send_time (ms)
                        item.SendTime = (long)ProtocolParser.ReadUInt64(stream);
                        break;
                    case 9: // quote_msg_id
                        item.QuoteMsgId = ProtocolParser.ReadString(stream);
                        break;
                    case 10: // msg_seq
                        item.MsgSeq = (long)ProtocolParser.ReadUInt64(stream);
                        break;
                    default:
                        ProtocolParser.SkipKey(stream, key);
                        break;
                }
            }

            return item;
        }

        private static void DecodeSender(Stream stream, ChatMessageItem target)
        {
            while (stream.Position < stream.Length)
            {
                var key = ProtocolParser.ReadKey(stream);
                if (key.Field == 0) break;

                switch (key.Field)
                {
                    case 1:
                        target.SenderId = ProtocolParser.ReadString(stream);
                        break;
                    case 3:
                        target.SenderName = ProtocolParser.ReadString(stream);
                        break;
                    case 4:
                        target.SenderAvatarUrl = ProtocolParser.ReadString(stream);
                        break;
                    default:
                        ProtocolParser.SkipKey(stream, key);
                        break;
                }
            }
        }

        private static void DecodeContent(Stream stream, ChatMessageItem target)
        {
            while (stream.Position < stream.Length)
            {
                var key = ProtocolParser.ReadKey(stream);
                if (key.Field == 0) break;

                switch (key.Field)
                {
                    case 1:
                        target.Text = ProtocolParser.ReadString(stream);
                        break;
                    case 3:
                        target.ImageUrl = ProtocolParser.ReadString(stream);
                        break;
                    case 4:
                        target.FileName = ProtocolParser.ReadString(stream);
                        break;
                    case 5:
                        target.FileUrl = ProtocolParser.ReadString(stream);
                        break;
                    case 8:
                        target.QuoteMsgText = ProtocolParser.ReadString(stream);
                        break;
                    case 9:
                        target.StickerUrl = ProtocolParser.ReadString(stream);
                        break;
                    case 37:
                        target.Tip = ProtocolParser.ReadString(stream);
                        break;
                    default:
                        ProtocolParser.SkipKey(stream, key);
                        break;
                }
            }
        }

        private static void DecodeStatusResponse(byte[] bytes, ApiResult target)
        {
            if (bytes == null || bytes.Length == 0) return;
            try
            {
                using (var stream = new MemoryStream(bytes))
                {
                    while (stream.Position < stream.Length)
                    {
                        var key = ProtocolParser.ReadKey(stream);
                        if (key.Field == 0) break;

                        if (key.Field == 1) // Status
                        {
                            using (var sub = ReadSubStream(stream))
                            {
                                DecodeStatus(sub, target);
                            }
                        }
                        else
                        {
                            ProtocolParser.SkipKey(stream, key);
                        }
                    }
                }
            }
            catch
            {
                target.Code = 0;
            }
        }

        private static MemoryStream ReadSubStream(Stream stream)
        {
            int length = (int)ProtocolParser.ReadUInt32(stream);
            byte[] buffer = new byte[length];
            int read = 0;
            while (read < length)
            {
                int r = stream.Read(buffer, read, length - read);
                if (r <= 0) break;
                read += r;
            }
            return new MemoryStream(buffer);
        }

        #endregion
    }
}
