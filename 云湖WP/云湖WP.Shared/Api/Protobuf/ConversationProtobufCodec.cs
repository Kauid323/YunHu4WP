using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Yh;
using 云湖WP.Api.Common;
using 云湖WP.Api.Conversation;

namespace 云湖WP.Api.Protobuf
{
    /// <summary>
    /// 会话列表 Protobuf 编解码器 (基于 SilentOrbit Protobuf 官方生成模型)
    /// </summary>
    public static class ConversationProtobufCodec
    {
        /// <summary>
        /// 编码 ConversationListRequest (POST /v1/conversation/list 请求体)
        /// </summary>
        public static byte[] EncodeListRequest(string md5 = "")
        {
            var req = new ConversationListRequest();
            if (!string.IsNullOrEmpty(md5))
            {
                req.Md5 = md5;
            }
            return ConversationListRequest.SerializeToBytes(req);
        }

        /// <summary>
        /// 解码 ConversationListResponse (POST /v1/conversation/list 响应体)
        /// </summary>
        public static ConversationListResult DecodeListResponse(byte[] bytes)
        {
            var result = new ConversationListResult();
            if (bytes == null || bytes.Length == 0) return result;

            if (bytes[0] == '{')
            {
                try
                {
                    string jsonStr = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                    Windows.Data.Json.JsonObject json;
                    if (Windows.Data.Json.JsonObject.TryParse(jsonStr, out json))
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
                var resp = ConversationListResponse.Deserialize(bytes);
                if (resp.Status != null)
                {
                    result.Code = resp.Status.Code;
                    result.Msg = resp.Status.Msg;
                }
                else
                {
                    result.Code = 0;
                }

                result.Total = resp.Total;
                result.Md5 = resp.Md5 ?? "";

                if (resp.DataField != null)
                {
                    foreach (var d in resp.DataField)
                    {
                        var item = new ConversationItem();
                        item.ChatId = d.ChatId ?? "";
                        item.ChatType = (int)d.ChatType;
                        item.Remark = d.Remark ?? "";
                        item.Name = d.Name ?? "";
                        item.ChatContent = d.ChatContent ?? "";
                        item.TimestampMs = (long)d.TimestampMs;
                        item.UnreadCount = d.UnreadMsg;
                        item.IsAt = d.At;
                        item.AvatarId = (long)d.AvatarId;
                        item.AvatarUrl = d.AvatarUrl ?? "";
                        item.DoNotDisturb = d.DoNotDisturb;
                        item.SendTimestamp = (long)d.SendTimestamp;
                        item.IsVip = d.IsVip;
                        item.CertificationLevel = d.CertificationLevel;

                        result.Conversations.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "Protobuf 解码异常: " + ex.Message;
            }

            return result;
        }
    }
}
