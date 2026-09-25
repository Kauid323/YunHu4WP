using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Windows.Data.Json;
using SilentOrbit.ProtocolBuffers;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.User.Info
{
    /// <summary>
    /// 用户详细资料展示模型
    /// </summary>
    public class UserDetailModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string AvatarUrl { get; set; }
        public bool IsVip { get; set; }
        public string RegisterTime { get; set; }
        public int OnlineDays { get; set; }
        public int ContinuousOnlineDays { get; set; }
        public int Gender { get; set; } // 1-男, 2-女, 3-其他, 0-未知
        public long Birthday { get; set; }
        public string Introduction { get; set; }
        public string LastActiveTime { get; set; }
        public string IpGeo { get; set; }
        public double Coin { get; set; }
        public string InvitationCode { get; set; }
        public List<string> Medals { get; set; }

        public UserDetailModel()
        {
            Medals = new List<string>();
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Name)) return Name;
                if (!string.IsNullOrEmpty(Id)) return Id;
                return "云湖用户";
            }
        }

        public string AvatarLetter
        {
            get
            {
                string n = DisplayName;
                return !string.IsNullOrEmpty(n) ? n.Substring(0, 1).ToUpper() : "云";
            }
        }

        public string GenderText
        {
            get
            {
                switch (Gender)
                {
                    case 1: return "男";
                    case 2: return "女";
                    case 3: return "其他";
                    default: return "保密";
                }
            }
        }

        public string BirthdayText
        {
            get
            {
                if (Birthday <= 0) return "未设置";
                try
                {
                    var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    var dt = epoch.AddSeconds(Birthday).ToLocalTime();
                    return dt.ToString("yyyy-MM-dd");
                }
                catch
                {
                    return "未设置";
                }
            }
        }

        public string VipText
        {
            get { return IsVip ? "VIP 会员" : "普通用户"; }
        }

        public string MedalsText
        {
            get
            {
                if (Medals == null || Medals.Count == 0) return "暂无勋章";
                return string.Join("、", Medals);
            }
        }
    }

    /// <summary>
    /// 用户资料查询结果
    /// </summary>
    public class UserDetailResult : ApiResult
    {
        public UserDetailModel Data { get; set; }
    }

    /// <summary>
    /// 用户详情页导航参数
    /// </summary>
    public class UserDetailNavArgs
    {
        public string UserId { get; set; }
        public string Name { get; set; }
        public string AvatarUrl { get; set; }
    }

    /// <summary>
    /// UserInfo Protobuf 编解码工具
    /// </summary>
    public static class UserInfoProtobufCodec
    {
        /// <summary>
        /// 编码 get_user_send 请求
        /// </summary>
        public static byte[] EncodeGetUserRequest(string userId)
        {
            using (var ms = new MemoryStream())
            {
                if (!string.IsNullOrEmpty(userId))
                {
                    ProtocolParser.WriteKey(ms, new Key(2, Wire.LengthDelimited));
                    ProtocolParser.WriteString(ms, userId);
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// 解码 get_user 响应
        /// </summary>
        public static UserDetailResult DecodeGetUserResponse(byte[] bytes)
        {
            var result = new UserDetailResult { Code = 1, Data = new UserDetailModel() };
            if (bytes == null || bytes.Length == 0) return result;

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
                        if (json.ContainsKey("data") && json.GetNamedValue("data").ValueType == JsonValueType.Object)
                        {
                            var d = json.GetNamedObject("data");
                            if (d.ContainsKey("id")) result.Data.Id = d.GetNamedString("id");
                            if (d.ContainsKey("name")) result.Data.Name = d.GetNamedString("name");
                            if (d.ContainsKey("avatar_url")) result.Data.AvatarUrl = d.GetNamedString("avatar_url");
                            if (d.ContainsKey("register_time")) result.Data.RegisterTime = d.GetNamedString("register_time");
                            if (d.ContainsKey("online_day")) result.Data.OnlineDays = (int)d.GetNamedNumber("online_day");
                            if (d.ContainsKey("continuous_online_day")) result.Data.ContinuousOnlineDays = (int)d.GetNamedNumber("continuous_online_day");
                            if (d.ContainsKey("is_vip")) result.Data.IsVip = d.GetNamedNumber("is_vip") == 1;
                            if (d.ContainsKey("ipGeo")) result.Data.IpGeo = d.GetNamedString("ipGeo");
                        }
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
                            case 2: // Data
                                using (var dataStream = ReadSubStream(stream))
                                {
                                    DecodeUserData(dataStream, result.Data);
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
                result.Msg = "解析用户资料异常: " + ex.Message;
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

        private static void DecodeUserData(Stream stream, UserDetailModel model)
        {
            while (stream.Position < stream.Length)
            {
                var key = ProtocolParser.ReadKey(stream);
                if (key.Field == 0) break;

                switch (key.Field)
                {
                    case 1:
                        model.Id = ProtocolParser.ReadString(stream);
                        break;
                    case 2:
                        model.Name = ProtocolParser.ReadString(stream);
                        break;
                    case 4:
                        model.AvatarUrl = ProtocolParser.ReadString(stream);
                        break;
                    case 6: // repeated Medal_info
                        using (var medalStream = ReadSubStream(stream))
                        {
                            string medalName = DecodeMedalName(medalStream);
                            if (!string.IsNullOrEmpty(medalName))
                            {
                                model.Medals.Add(medalName);
                            }
                        }
                        break;
                    case 7:
                        model.RegisterTime = ProtocolParser.ReadString(stream);
                        break;
                    case 11:
                        model.OnlineDays = (int)ProtocolParser.ReadUInt32(stream);
                        break;
                    case 12:
                        model.ContinuousOnlineDays = (int)ProtocolParser.ReadUInt32(stream);
                        break;
                    case 13:
                        model.IsVip = ProtocolParser.ReadUInt32(stream) == 1;
                        break;
                    case 19: // ProfileInfo
                        using (var profileStream = ReadSubStream(stream))
                        {
                            DecodeProfileInfo(profileStream, model);
                        }
                        break;
                    case 20:
                        model.IpGeo = ProtocolParser.ReadString(stream);
                        break;
                    default:
                        ProtocolParser.SkipKey(stream, key);
                        break;
                }
            }
        }

        private static string DecodeMedalName(Stream stream)
        {
            string name = null;
            while (stream.Position < stream.Length)
            {
                var key = ProtocolParser.ReadKey(stream);
                if (key.Field == 0) break;

                if (key.Field == 2)
                {
                    name = ProtocolParser.ReadString(stream);
                }
                else
                {
                    ProtocolParser.SkipKey(stream, key);
                }
            }
            return name;
        }

        private static void DecodeProfileInfo(Stream stream, UserDetailModel model)
        {
            while (stream.Position < stream.Length)
            {
                var key = ProtocolParser.ReadKey(stream);
                if (key.Field == 0) break;

                switch (key.Field)
                {
                    case 1:
                        model.LastActiveTime = ProtocolParser.ReadString(stream);
                        break;
                    case 2:
                        model.Introduction = ProtocolParser.ReadString(stream);
                        break;
                    case 3:
                        model.Gender = (int)ProtocolParser.ReadUInt32(stream);
                        break;
                    case 4:
                        model.Birthday = (long)ProtocolParser.ReadUInt64(stream);
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
            return new MemoryStream(buffer);
        }
    }
}
