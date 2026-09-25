using System;
using System.IO;
using System.Text;
using Windows.Data.Json;
using SilentOrbit.ProtocolBuffers;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.User.Self
{
    /// <summary>
    /// 当前登录用户自身详细信息模型
    /// </summary>
    public class UserSelfInfoModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string AvatarUrl { get; set; }
        public long AvatarId { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public double Coin { get; set; }
        public bool IsVip { get; set; }
        public long VipExpiredTimestamp { get; set; }
        public int VipStatus { get; set; }
        public string InvitationCode { get; set; }
        public string RegisterTime { get; set; }
        public string IpGeo { get; set; }
        public int Gender { get; set; }
        public long Birthday { get; set; }
        public string Introduction { get; set; }
        public long LastLoginTime { get; set; }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Name)) return Name;
                if (!string.IsNullOrEmpty(Id)) return "UID: " + Id;
                return "云湖用户";
            }
        }

        public string AvatarLetter
        {
            get
            {
                string name = DisplayName;
                if (!string.IsNullOrEmpty(name))
                {
                    return name.Substring(0, 1).ToUpper();
                }
                return "云";
            }
        }
    }

    /// <summary>
    /// 用户自身信息响应结果模型 (/v1/user/info)
    /// </summary>
    public class UserSelfInfoResult : ApiResult
    {
        public UserSelfInfoModel Data { get; set; }

        // 快捷访问属性（兼容已有调用）
        public string Id
        {
            get { return Data != null ? Data.Id : ""; }
            set
            {
                EnsureData();
                Data.Id = value;
            }
        }

        public string Name
        {
            get { return Data != null ? Data.Name : ""; }
            set
            {
                EnsureData();
                Data.Name = value;
            }
        }

        public string AvatarUrl
        {
            get { return Data != null ? Data.AvatarUrl : ""; }
            set
            {
                EnsureData();
                Data.AvatarUrl = value;
            }
        }

        public string Phone
        {
            get { return Data != null ? Data.Phone : ""; }
            set
            {
                EnsureData();
                Data.Phone = value;
            }
        }

        public string Email
        {
            get { return Data != null ? Data.Email : ""; }
            set
            {
                EnsureData();
                Data.Email = value;
            }
        }

        public double Coin
        {
            get { return Data != null ? Data.Coin : 0; }
            set
            {
                EnsureData();
                Data.Coin = value;
            }
        }

        public bool IsVip
        {
            get { return Data != null ? Data.IsVip : false; }
            set
            {
                EnsureData();
                Data.IsVip = value;
            }
        }

        public UserSelfInfoResult()
        {
            Data = new UserSelfInfoModel();
            Code = 1;
        }

        private void EnsureData()
        {
            if (Data == null) Data = new UserSelfInfoModel();
        }
    }

    /// <summary>
    /// 用户自身信息编解码器 (支持 Protobuf 与 JSON 自动适配)
    /// </summary>
    public static class UserSelfProtobufCodec
    {
        /// <summary>
        /// 解码用户自身信息响应 (GET /v1/user/info)
        /// </summary>
        public static UserSelfInfoResult DecodeSelfInfoResponse(byte[] bytes)
        {
            var result = new UserSelfInfoResult();
            if (bytes == null || bytes.Length == 0) return result;

            // 1. JSON 响应适配
            if (bytes[0] == '{')
            {
                string jsonStr = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                return DecodeSelfInfoJson(jsonStr);
            }

            // 2. Protobuf 二进制流解析
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
                            case 2: // Data
                                using (var dataStream = ReadSubStream(stream))
                                {
                                    DecodeSelfData(dataStream, result.Data);
                                }
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
                result.Msg = "Protobuf 解码失败: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// JSON 字符串安全解码 (支持 snake_case、camelCase 与类型自适应)
        /// </summary>
        public static UserSelfInfoResult DecodeSelfInfoJson(string jsonStr)
        {
            var result = new UserSelfInfoResult();
            if (string.IsNullOrEmpty(jsonStr)) return result;

            try
            {
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)SafeGetNumber(root, "code");
                    if (root.ContainsKey("msg")) result.Msg = SafeGetString(root, "msg");
                    if (root.ContainsKey("data") && root.GetNamedValue("data").ValueType == JsonValueType.Object)
                    {
                        var data = root.GetNamedObject("data");
                        result.Data.Id = FirstNonEmpty(SafeGetString(data, "id"), SafeGetString(data, "userId"), SafeGetString(data, "uid"));
                        result.Data.Name = FirstNonEmpty(SafeGetString(data, "name"), SafeGetString(data, "nickname"), SafeGetString(data, "username"));
                        result.Data.AvatarUrl = FirstNonEmpty(SafeGetString(data, "avatar_url"), SafeGetString(data, "avatarUrl"), SafeGetString(data, "avatar"));
                        result.Data.Phone = FirstNonEmpty(SafeGetString(data, "phone"), SafeGetString(data, "mobile"));
                        result.Data.Email = SafeGetString(data, "email");
                        result.Data.Coin = SafeGetNumber(data, "coin", "coins", "gold");
                        result.Data.IsVip = SafeGetBool(data, "is_vip", "isVip", "vip");
                        result.Data.RegisterTime = FirstNonEmpty(SafeGetString(data, "register_time"), SafeGetString(data, "registerTime"));
                        result.Data.IpGeo = FirstNonEmpty(SafeGetString(data, "ipGeo"), SafeGetString(data, "ip_geo"), SafeGetString(data, "ip"));
                        result.Data.Introduction = FirstNonEmpty(SafeGetString(data, "introduction"), SafeGetString(data, "intro"), SafeGetString(data, "sign"));
                        result.Data.Gender = (int)SafeGetNumber(data, "gender", "sex");
                        result.Data.Birthday = (long)SafeGetNumber(data, "birthday");
                        result.Data.LastLoginTime = (long)SafeGetNumber(data, "lastLoginTime", "last_login_time");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "JSON 解析失败: " + ex.Message;
            }

            return result;
        }

        private static string SafeGetString(JsonObject obj, string key)
        {
            if (obj == null || !obj.ContainsKey(key)) return "";
            var val = obj.GetNamedValue(key);
            if (val.ValueType == JsonValueType.String) return val.GetString();
            if (val.ValueType == JsonValueType.Number) return val.GetNumber().ToString();
            if (val.ValueType == JsonValueType.Boolean) return val.GetBoolean().ToString();
            return "";
        }

        private static double SafeGetNumber(JsonObject obj, params string[] keys)
        {
            if (obj == null || keys == null) return 0;
            foreach (var key in keys)
            {
                if (obj.ContainsKey(key))
                {
                    var val = obj.GetNamedValue(key);
                    if (val.ValueType == JsonValueType.Number) return val.GetNumber();
                    if (val.ValueType == JsonValueType.String)
                    {
                        double n;
                        if (double.TryParse(val.GetString(), out n)) return n;
                    }
                    if (val.ValueType == JsonValueType.Boolean) return val.GetBoolean() ? 1 : 0;
                }
            }
            return 0;
        }

        private static bool SafeGetBool(JsonObject obj, params string[] keys)
        {
            if (obj == null || keys == null) return false;
            foreach (var key in keys)
            {
                if (obj.ContainsKey(key))
                {
                    var val = obj.GetNamedValue(key);
                    if (val.ValueType == JsonValueType.Boolean) return val.GetBoolean();
                    if (val.ValueType == JsonValueType.Number) return val.GetNumber() > 0;
                    if (val.ValueType == JsonValueType.String)
                    {
                        string s = val.GetString();
                        return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            return false;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return "";
            foreach (var v in values)
            {
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return "";
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

        private static void DecodeSelfData(Stream stream, UserSelfInfoModel model)
        {
            while (stream.Position < stream.Length)
            {
                var key = ProtocolParser.ReadKey(stream);
                if (key.Field == 0) break;

                switch (key.Field)
                {
                    case 1: // id (string)
                        model.Id = ProtocolParser.ReadString(stream);
                        break;
                    case 2: // name (string)
                        model.Name = ProtocolParser.ReadString(stream);
                        break;
                    case 3: // field3 (int32)
                        ProtocolParser.ReadUInt32(stream);
                        break;
                    case 4: // avatar_url (string)
                        model.AvatarUrl = ProtocolParser.ReadString(stream);
                        break;
                    case 5: // avatar_id (int64)
                        model.AvatarId = (long)ProtocolParser.ReadUInt64(stream);
                        break;
                    case 6: // phone (string)
                        model.Phone = ProtocolParser.ReadString(stream);
                        break;
                    case 7: // email (string)
                        model.Email = ProtocolParser.ReadString(stream);
                        break;
                    case 8: // coin (double)
                        if (key.WireType == Wire.Fixed64)
                        {
                            byte[] b = new byte[8];
                            stream.Read(b, 0, 8);
                            model.Coin = BitConverter.ToDouble(b, 0);
                        }
                        else if (key.WireType == Wire.Fixed32)
                        {
                            byte[] b = new byte[4];
                            stream.Read(b, 0, 4);
                            model.Coin = BitConverter.ToSingle(b, 0);
                        }
                        else if (key.WireType == Wire.Varint)
                        {
                            model.Coin = ProtocolParser.ReadUInt64(stream);
                        }
                        else if (key.WireType == Wire.LengthDelimited)
                        {
                            string s = ProtocolParser.ReadString(stream);
                            double c;
                            if (double.TryParse(s, out c)) model.Coin = c;
                        }
                        else
                        {
                            ProtocolParser.SkipKey(stream, key);
                        }
                        break;
                    case 9: // is_vip (bool)
                        model.IsVip = ProtocolParser.ReadBool(stream);
                        break;
                    case 10: // vip_expired_timestamp (int64)
                        model.VipExpiredTimestamp = (long)ProtocolParser.ReadUInt64(stream);
                        break;
                    case 11: // vip_status (VipStatus enum / int32)
                        model.VipStatus = (int)ProtocolParser.ReadUInt32(stream);
                        if (model.VipStatus == 1) model.IsVip = true;
                        break;
                    case 12: // invitation_code (string)
                        model.InvitationCode = ProtocolParser.ReadString(stream);
                        break;
                    default:
                        ProtocolParser.SkipKey(stream, key);
                        break;
                }
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
            return new MemoryStream(buffer, 0, read);
        }
    }
}
