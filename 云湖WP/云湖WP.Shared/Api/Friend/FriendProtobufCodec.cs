using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Windows.Data.Json;
using 云湖WP.Api.Protobuf;

namespace 云湖WP.Api.Friend
{
    /// <summary>
    /// 通讯录 Protobuf 与 JSON 自动编解码器 (POST /v1/friend/address-book-list)
    /// </summary>
    public static class FriendProtobufCodec
    {
        /// <summary>
        /// 构造 address_book_list_send 请求体 (Protobuf: tag 2 number = "通讯录请求")
        /// </summary>
        public static byte[] EncodeAddressBookRequest()
        {
            using (var ms = new MemoryStream())
            {
                ProtobufWriter.WriteString(ms, 2, "通讯录请求");
                return ms.ToArray();
            }
        }

        /// <summary>
        /// 解码 address_book_list 响应体 (支持 Protobuf 二进制与 JSON 自动适配)
        /// </summary>
        public static AddressBookResult DecodeAddressBookResponse(byte[] bytes)
        {
            var result = new AddressBookResult();
            if (bytes == null || bytes.Length == 0)
            {
                result.Code = -1;
                result.Msg = "响应数据为空";
                return result;
            }

            // 1. JSON 响应自动降级适配
            if (bytes[0] == '{')
            {
                try
                {
                    string jsonStr = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                    JsonObject root;
                    if (JsonObject.TryParse(jsonStr, out root))
                    {
                        if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                        if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                        if (root.ContainsKey("message")) result.Msg = root.GetNamedString("message");

                        if (root.ContainsKey("data") && root.GetNamedValue("data").ValueType == JsonValueType.Array)
                        {
                            var dataArray = root.GetNamedArray("data");
                            foreach (var groupVal in dataArray)
                            {
                                if (groupVal.ValueType != JsonValueType.Object) continue;
                                var groupObj = groupVal.GetObject();
                                string listName = groupObj.ContainsKey("list_name") ? groupObj.GetNamedString("list_name") : 
                                                 (groupObj.ContainsKey("listName") ? groupObj.GetNamedString("listName") : "");

                                if (groupObj.ContainsKey("data") && groupObj.GetNamedValue("data").ValueType == JsonValueType.Array)
                                {
                                    var itemArray = groupObj.GetNamedArray("data");
                                    foreach (var itemVal in itemArray)
                                    {
                                        if (itemVal.ValueType != JsonValueType.Object) continue;
                                        var itemObj = itemVal.GetObject();

                                        var contact = new FriendContactItem();
                                        if (itemObj.ContainsKey("chat_id")) contact.ChatId = itemObj.GetNamedString("chat_id");
                                        else if (itemObj.ContainsKey("chatId")) contact.ChatId = itemObj.GetNamedString("chatId");

                                        if (itemObj.ContainsKey("name")) contact.Name = itemObj.GetNamedString("name");
                                        if (itemObj.ContainsKey("avatar_url")) contact.AvatarUrl = itemObj.GetNamedString("avatar_url");
                                        else if (itemObj.ContainsKey("avatarUrl")) contact.AvatarUrl = itemObj.GetNamedString("avatarUrl");

                                        if (itemObj.ContainsKey("permisson_level")) contact.PermissionLevel = (int)itemObj.GetNamedNumber("permisson_level");
                                        else if (itemObj.ContainsKey("permissionLevel")) contact.PermissionLevel = (int)itemObj.GetNamedNumber("permissionLevel");

                                        if (itemObj.ContainsKey("noDisturb")) contact.NoDisturb = itemObj.GetNamedBoolean("noDisturb");

                                        CategorizeContact(result, listName, contact);
                                    }
                                }
                            }
                        }
                        return result;
                    }
                }
                catch { }
            }

            // 2. 原生 Protobuf 二进制解码
            try
            {
                using (var ms = new MemoryStream(bytes))
                {
                    while (ms.Position < ms.Length)
                    {
                        ulong key = ProtobufReader.ReadVarint(ms);
                        int fieldNumber = (int)(key >> 3);
                        int wireType = (int)(key & 0x07);

                        if (fieldNumber == 1 && wireType == 2) // Status status
                        {
                            byte[] statusBytes = ProtobufReader.ReadBytes(ms);
                            DecodeStatus(statusBytes, result);
                        }
                        else if (fieldNumber == 2 && wireType == 2) // repeated Data data (Group)
                        {
                            byte[] groupBytes = ProtobufReader.ReadBytes(ms);
                            DecodeGroup(groupBytes, result);
                        }
                        else
                        {
                            ProtobufReader.SkipField(ms, wireType);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "Protobuf 解码通讯录异常: " + ex.Message;
            }

            return result;
        }

        private static void DecodeStatus(byte[] bytes, AddressBookResult result)
        {
            if (bytes == null || bytes.Length == 0) return;
            using (var ms = new MemoryStream(bytes))
            {
                while (ms.Position < ms.Length)
                {
                    ulong key = ProtobufReader.ReadVarint(ms);
                    int fieldNumber = (int)(key >> 3);
                    int wireType = (int)(key & 0x07);

                    if (fieldNumber == 2 && wireType == 0) // code
                    {
                        result.Code = (int)ProtobufReader.ReadVarint(ms);
                    }
                    else if (fieldNumber == 3 && wireType == 2) // msg
                    {
                        result.Msg = ProtobufReader.ReadString(ms);
                    }
                    else
                    {
                        ProtobufReader.SkipField(ms, wireType);
                    }
                }
            }
        }

        private static void DecodeGroup(byte[] bytes, AddressBookResult result)
        {
            if (bytes == null || bytes.Length == 0) return;
            string listName = "";

            using (var ms = new MemoryStream(bytes))
            {
                while (ms.Position < ms.Length)
                {
                    ulong key = ProtobufReader.ReadVarint(ms);
                    int fieldNumber = (int)(key >> 3);
                    int wireType = (int)(key & 0x07);

                    if (fieldNumber == 1 && wireType == 2) // list_name
                    {
                        listName = ProtobufReader.ReadString(ms);
                    }
                    else if (fieldNumber == 2 && wireType == 2) // repeated Data_list
                    {
                        byte[] itemBytes = ProtobufReader.ReadBytes(ms);
                        var contact = DecodeContactItem(itemBytes);
                        if (contact != null)
                        {
                            CategorizeContact(result, listName, contact);
                        }
                    }
                    else
                    {
                        ProtobufReader.SkipField(ms, wireType);
                    }
                }
            }
        }

        private static FriendContactItem DecodeContactItem(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var item = new FriendContactItem();

            using (var ms = new MemoryStream(bytes))
            {
                while (ms.Position < ms.Length)
                {
                    ulong key = ProtobufReader.ReadVarint(ms);
                    int fieldNumber = (int)(key >> 3);
                    int wireType = (int)(key & 0x07);

                    if (fieldNumber == 1 && wireType == 2) // chat_id
                    {
                        item.ChatId = ProtobufReader.ReadString(ms);
                    }
                    else if (fieldNumber == 2 && wireType == 2) // name
                    {
                        item.Name = ProtobufReader.ReadString(ms);
                    }
                    else if (fieldNumber == 3 && wireType == 2) // avatar_url
                    {
                        item.AvatarUrl = ProtobufReader.ReadString(ms);
                    }
                    else if (fieldNumber == 4 && wireType == 0) // permisson_level
                    {
                        item.PermissionLevel = (int)ProtobufReader.ReadVarint(ms);
                    }
                    else if (fieldNumber == 5 && wireType == 0) // noDisturb
                    {
                        item.NoDisturb = ProtobufReader.ReadVarint(ms) != 0;
                    }
                    else
                    {
                        ProtobufReader.SkipField(ms, wireType);
                    }
                }
            }

            return item;
        }

        private static void CategorizeContact(AddressBookResult result, string listName, FriendContactItem contact)
        {
            if (contact == null) return;

            if (listName == "我加入的群聊" || listName.Contains("群"))
            {
                contact.ChatType = 2; // 群聊
                result.Groups.Add(contact);
            }
            else if (listName == "机器人" || listName.Contains("机"))
            {
                contact.ChatType = 3; // 机器人
                result.Bots.Add(contact);
            }
            else
            {
                contact.ChatType = 1; // 用户/好友
                result.Friends.Add(contact);
            }
        }
    }
}
