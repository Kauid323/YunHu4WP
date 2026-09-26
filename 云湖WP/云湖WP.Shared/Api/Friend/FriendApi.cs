using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;
using 云湖WP.Utils;

namespace 云湖WP.Api.Friend
{
    /// <summary>
    /// 好友与通讯录相关 API (/v1/friend)
    /// </summary>
    public static class FriendApi
    {
        private static readonly HashSet<string> _knownContactIds = new HashSet<string>();
        private static DateTime _lastContactFetchTime = DateTime.MinValue;
        private static readonly object _contactLock = new object();

        /// <summary>
        /// 检查指定会话/用户/群聊/机器人是否存在于通讯录中
        /// </summary>
        public static async Task<bool> IsContactExistsAsync(string token, string chatId, int chatType)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId)) return false;

            lock (_contactLock)
            {
                if ((DateTime.Now - _lastContactFetchTime).TotalSeconds < 60 && _knownContactIds.Count > 0)
                {
                    return _knownContactIds.Contains(chatId);
                }
            }

            var result = await GetAddressBookListAsync(token);
            if (result != null && result.IsSuccess)
            {
                lock (_contactLock)
                {
                    return _knownContactIds.Contains(chatId);
                }
            }

            return false;
        }

        /// <summary>
        /// 获取所有聊天对象通讯录 (POST /v1/friend/address-book-list，支持 Protobuf 二进制与 JSON 自动适配)
        /// </summary>
        public static async Task<AddressBookResult> GetAddressBookListAsync(string token)
        {
            var result = new AddressBookResult();
            if (string.IsNullOrEmpty(token))
            {
                result.Code = -1;
                result.Msg = "Token 为空";
                return result;
            }

            try
            {
                // 构建 Protobuf 请求数据: address_book_list_send
                byte[] reqBytes = FriendProtobufCodec.EncodeAddressBookRequest();

                // 发送 Protobuf POST 请求
                byte[] respBytes = await HttpHelper.PostProtobufAsync("/v1/friend/address-book-list", reqBytes, token);
                if (respBytes != null && respBytes.Length > 0)
                {
                    result = FriendProtobufCodec.DecodeAddressBookResponse(respBytes);
                    AppLogger.Log("FriendApi", string.Format("GetAddressBookListAsync decoded: Friends={0}, Groups={1}, Bots={2}",
                        result.Friends.Count, result.Groups.Count, result.Bots.Count));

                    lock (_contactLock)
                    {
                        _knownContactIds.Clear();
                        if (result.Friends != null)
                        {
                            foreach (var f in result.Friends)
                            {
                                if (!string.IsNullOrEmpty(f.ChatId)) _knownContactIds.Add(f.ChatId);
                            }
                        }
                        if (result.Groups != null)
                        {
                            foreach (var g in result.Groups)
                            {
                                if (!string.IsNullOrEmpty(g.ChatId)) _knownContactIds.Add(g.ChatId);
                            }
                        }
                        if (result.Bots != null)
                        {
                            foreach (var b in result.Bots)
                            {
                                if (!string.IsNullOrEmpty(b.ChatId)) _knownContactIds.Add(b.ChatId);
                            }
                        }
                        _lastContactFetchTime = DateTime.Now;
                    }

                    return result;
                }
                else
                {
                    result.Code = -1;
                    result.Msg = "服务器返回空响应";
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取通讯录异常: " + ex.Message;
                AppLogger.Log("FriendApi", "GetAddressBookListAsync error: " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// 申请添加好友/群聊/机器人 (POST /v1/friend/add-friend)
        /// </summary>
        public static async Task<ApiResult> AddFriendAsync(string token, string chatId, int chatType, string remark = "")
        {
            var result = new ApiResult();
            if (string.IsNullOrEmpty(token))
            {
                result.Code = -1;
                result.Msg = "Token 为空";
                return result;
            }

            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("chatId", JsonValue.CreateStringValue(chatId));
                reqObj.SetNamedValue("chatType", JsonValue.CreateNumberValue(chatType));
                if (!string.IsNullOrEmpty(remark))
                {
                    reqObj.SetNamedValue("remark", JsonValue.CreateStringValue(remark));
                }

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/friend/add-friend", reqObj.Stringify(), token);
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                }

                if (result.Code == 1)
                {
                    lock (_contactLock)
                    {
                        _knownContactIds.Add(chatId);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "申请添加失败: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 删除好友/退出群聊/移除机器人 (POST /v1/friend/delete-friend)
        /// </summary>
        public static async Task<ApiResult> DeleteFriendAsync(string token, string chatId, int chatType)
        {
            var result = new ApiResult();
            if (string.IsNullOrEmpty(token))
            {
                result.Code = -1;
                result.Msg = "Token 为空";
                return result;
            }

            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("chatId", JsonValue.CreateStringValue(chatId));
                reqObj.SetNamedValue("chatType", JsonValue.CreateNumberValue(chatType));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/friend/delete-friend", reqObj.Stringify(), token);
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                }

                if (result.Code == 1)
                {
                    lock (_contactLock)
                    {
                        _knownContactIds.Remove(chatId);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "删除失败: " + ex.Message;
            }

            return result;
        }
    }
}
