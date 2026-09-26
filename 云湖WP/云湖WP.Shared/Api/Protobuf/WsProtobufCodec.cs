using System;
using System.IO;
using System.Text;
using SilentOrbit.ProtocolBuffers;
using 云湖WP.Api.Message;
using 云湖WP.Utils;

namespace 云湖WP.Api.Protobuf
{
    public class WsFrameInfo
    {
        public string Seq { get; set; }
        public string Cmd { get; set; }
    }

    public class WsDecodedMessage
    {
        public string Cmd { get; set; }
        public string Seq { get; set; }
        public ChatMessageItem MessageItem { get; set; }
        public long DeleteTime { get; set; }
        public long EditTime { get; set; }
        public string DraftInput { get; set; }
        public string DraftChatId { get; set; }
    }

    /// <summary>
    /// 云湖 WebSocket Protobuf 二进制帧解析器 (映射 chat_ws_go.proto)
    /// </summary>
    public static class WsProtobufCodec
    {
        /// <summary>
        /// 解码 WebSocket 接收到的二进制流
        /// </summary>
        public static WsDecodedMessage DecodeFrame(byte[] bytes, string currentUserId = "")
        {
            if (bytes == null || bytes.Length == 0) return null;

            var result = new WsDecodedMessage();
            try
            {
                using (var ms = new MemoryStream(bytes))
                {
                    while (ms.Position < ms.Length)
                    {
                        var key = ProtocolParser.ReadKey(ms);
                        if (key.Field == 0) break;
                        switch (key.Field)
                        {
                            case 1: // info (INFO)
                                using (var infoSub = new MemoryStream(ProtocolParser.ReadBytes(ms)))
                                {
                                    var info = ParseInfo(infoSub);
                                    result.Cmd = info.Cmd;
                                    result.Seq = info.Seq;
                                }
                                break;

                            case 2: // data
                                byte[] dataBytes = ProtocolParser.ReadBytes(ms);
                                if (result.Cmd == "push_message" || result.Cmd == "edit_message")
                                {
                                    using (var dataSub = new MemoryStream(dataBytes))
                                    {
                                        while (dataSub.Position < dataSub.Length)
                                        {
                                            var dataKey = ProtocolParser.ReadKey(dataSub);
                                            if (dataKey.Field == 0) break;
                                            switch (dataKey.Field)
                                            {
                                                case 2: // msg (WsMsg)
                                                    using (var msgSub = new MemoryStream(ProtocolParser.ReadBytes(dataSub)))
                                                    {
                                                        result.MessageItem = ParseWsMsg(msgSub, currentUserId);
                                                    }
                                                    break;
                                                default:
                                                    ProtocolParser.SkipKey(dataSub, dataKey);
                                                    break;
                                            }
                                        }
                                    }
                                }
                                else if (result.Cmd == "draft_input")
                                {
                                    using (var dataSub = new MemoryStream(dataBytes))
                                    {
                                        while (dataSub.Position < dataSub.Length)
                                        {
                                            var dataKey = ProtocolParser.ReadKey(dataSub);
                                            if (dataKey.Field == 0) break;
                                            if (dataKey.Field == 2) // draft
                                            {
                                                using (var draftSub = new MemoryStream(ProtocolParser.ReadBytes(dataSub)))
                                                {
                                                    while (draftSub.Position < draftSub.Length)
                                                    {
                                                        var dKey = ProtocolParser.ReadKey(draftSub);
                                                        if (dKey.Field == 0) break;
                                                        if (dKey.Field == 1) result.DraftChatId = ProtocolParser.ReadString(draftSub);
                                                        else if (dKey.Field == 2) result.DraftInput = ProtocolParser.ReadString(draftSub);
                                                        else ProtocolParser.SkipKey(draftSub, dKey);
                                                    }
                                                }
                                            }
                                            else ProtocolParser.SkipKey(dataSub, dataKey);
                                        }
                                    }
                                }
                                break;

                            default:
                                ProtocolParser.SkipKey(ms, key);
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("WsCodec", "DecodeFrame error: " + ex.Message);
            }

            return result;
        }

        private static WsFrameInfo ParseInfo(Stream stream)
        {
            var info = new WsFrameInfo();
            while (stream.Position < stream.Length)
            {
                var key = ProtocolParser.ReadKey(stream);
                if (key.Field == 0) break;
                switch (key.Field)
                {
                    case 1:
                        info.Seq = ProtocolParser.ReadString(stream);
                        break;
                    case 2:
                        info.Cmd = ProtocolParser.ReadString(stream);
                        break;
                    default:
                        ProtocolParser.SkipKey(stream, key);
                        break;
                }
            }
            return info;
        }

        private static ChatMessageItem ParseWsMsg(Stream stream, string currentUserId)
        {
            var item = new ChatMessageItem();
            item.Direction = "left";
            item.ContentType = 1;

            while (stream.Position < stream.Length)
            {
                var key = ProtocolParser.ReadKey(stream);
                if (key.Field == 0) break;
                switch (key.Field)
                {
                    case 1: // msg_id
                        item.MsgId = ProtocolParser.ReadString(stream);
                        break;
                    case 2: // sender (WsSender)
                        using (var sub = new MemoryStream(ProtocolParser.ReadBytes(stream)))
                        {
                            ParseWsSender(sub, item);
                        }
                        break;
                    case 3: // recv_id
                        ProtocolParser.ReadString(stream);
                        break;
                    case 4: // chat_id
                        item.ChatId = ProtocolParser.ReadString(stream);
                        break;
                    case 5: // chat_type
                        item.ChatType = (int)ProtocolParser.ReadUInt64(stream);
                        break;
                    case 6: // content (WsContent)
                        using (var sub = new MemoryStream(ProtocolParser.ReadBytes(stream)))
                        {
                            ParseWsContent(sub, item);
                        }
                        break;
                    case 7: // content_type
                        item.ContentType = (int)ProtocolParser.ReadUInt64(stream);
                        break;
                    case 8: // timestamp (ms)
                        item.SendTime = (long)ProtocolParser.ReadUInt64(stream);
                        break;
                    case 10: // delete_time (ms)
                        long delTime = (long)ProtocolParser.ReadUInt64(stream);
                        break;
                    case 11: // quote_msg_id
                        item.QuoteMsgId = ProtocolParser.ReadString(stream);
                        break;
                    case 12: // msg_seq
                        item.MsgSeq = (long)ProtocolParser.ReadUInt64(stream);
                        break;
                    case 14: // edit_time
                        long editTime = (long)ProtocolParser.ReadUInt64(stream);
                        break;
                    default:
                        ProtocolParser.SkipKey(stream, key);
                        break;
                }
            }

            if (!string.IsNullOrEmpty(currentUserId) && !string.IsNullOrEmpty(item.SenderId) && item.SenderId == currentUserId)
            {
                item.Direction = "right";
            }

            item.InitParsedData();
            return item;
        }

        private static void ParseWsSender(Stream stream, ChatMessageItem item)
        {
            while (stream.Position < stream.Length)
            {
                var key = ProtocolParser.ReadKey(stream);
                if (key.Field == 0) break;
                switch (key.Field)
                {
                    case 1: // chat_id
                        item.SenderId = ProtocolParser.ReadString(stream);
                        break;
                    case 2: // chat_type
                        ProtocolParser.ReadUInt64(stream);
                        break;
                    case 3: // name
                        item.SenderName = ProtocolParser.ReadString(stream);
                        break;
                    case 4: // avatar_url
                        item.SenderAvatarUrl = ProtocolParser.ReadString(stream);
                        break;
                    default:
                        ProtocolParser.SkipKey(stream, key);
                        break;
                }
            }
        }

        private static void ParseWsContent(Stream stream, ChatMessageItem item)
        {
            while (stream.Position < stream.Length)
            {
                var key = ProtocolParser.ReadKey(stream);
                if (key.Field == 0) break;
                switch (key.Field)
                {
                    case 1: // text
                        item.Text = ProtocolParser.ReadString(stream);
                        break;
                    case 2: // buttons
                        ProtocolParser.ReadString(stream);
                        break;
                    case 3: // image_url
                        item.ImageUrl = ProtocolParser.ReadString(stream);
                        break;
                    case 4: // file_name
                        item.FileName = ProtocolParser.ReadString(stream);
                        break;
                    case 5: // file_url
                        item.FileUrl = ProtocolParser.ReadString(stream);
                        break;
                    case 8: // quote_msg_text
                        item.QuoteMsgText = ProtocolParser.ReadString(stream);
                        break;
                    case 9: // sticker_url
                        item.StickerUrl = ProtocolParser.ReadString(stream);
                        break;
                    case 15: // expression_id
                        ProtocolParser.ReadString(stream);
                        break;
                    case 18: // file_size
                        item.FileSize = (long)ProtocolParser.ReadUInt64(stream);
                        break;
                    case 19: // video_url
                        item.VideoUrl = ProtocolParser.ReadString(stream);
                        break;
                    case 20: // video_time
                        item.VideoDuration = (int)ProtocolParser.ReadUInt64(stream);
                        break;
                    case 21: // audio_url
                        item.AudioUrl = ProtocolParser.ReadString(stream);
                        break;
                    case 22: // audio_time
                        item.AudioDuration = (int)ProtocolParser.ReadUInt64(stream);
                        break;
                    case 33: // width
                        item.MediaWidth = (int)ProtocolParser.ReadUInt64(stream);
                        break;
                    case 34: // height
                        item.MediaHeight = (int)ProtocolParser.ReadUInt64(stream);
                        break;
                    default:
                        ProtocolParser.SkipKey(stream, key);
                        break;
                }
            }
        }
    }
}
