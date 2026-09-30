using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;
using 云湖WP.Api.Protobuf;
using 云湖WP.Utils;

namespace 云湖WP.Api.Group
{
    /// <summary>群聊详情、成员与管理 API。详情/成员/编辑使用官方 v1 protobuf 协议。</summary>
    public static class GroupApi
    {
        public static async Task<GroupInfoResult> GetInfoAsync(string token, string groupId)
        {
            var result = new GroupInfoResult();
            try
            {
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(groupId)) return Fail<GroupInfoResult>(result, "Token 或群聊 ID 为空");
                AppLogger.Log("GroupApi", string.Format("GetInfoAsync 请求开始: GroupId={0}", groupId));
                var bytes = await HttpHelper.PostProtobufAsync("/v1/group/info", Yh.GroupInfoRequest.SerializeToBytes(new Yh.GroupInfoRequest { GroupId = groupId }), token);
                if (bytes == null || bytes.Length == 0)
                {
                    AppLogger.Log("GroupApi", "GetInfoAsync 响应为空 (bytes is null or empty)");
                    return Fail<GroupInfoResult>(result, "服务器返回空数据");
                }

                string hexPrefix = BitConverter.ToString(bytes, 0, Math.Min(bytes.Length, 24));
                AppLogger.Log("GroupApi", string.Format("GetInfoAsync 收到响应: Length={0} bytes, HexPrefix={1}", bytes.Length, hexPrefix));

                // 核心解析：使用基于 ProtobufReader 的安全流式解码器
                // 自动识别外层 Status/GroupData 结构，容错跳过未知/新增字段，避免 SilentOrbit 抛 Invalid field id: 0
                int statusCode = 0;
                string statusMsg = "";
                var group = ParseGroupInfoSafely(bytes, out statusCode, out statusMsg);

                result.Code = statusCode;
                result.Msg = statusMsg;
                result.Group = group;

                if (result.Group != null && !string.IsNullOrEmpty(result.Group.GroupId))
                {
                    AppLogger.Log("GroupApi", string.Format("📋 群详情解析成功: GroupId={0}, Name='{1}', Owner={2}, Headcount={3}, PermissionLevel={4}({5}), Category='{6}', Code='{7}', Limited='{8}', Top={9}, HideMembers={10}, DenyUpload={11}",
                        result.Group.GroupId, result.Group.Name, result.Group.Owner, result.Group.Headcount, result.Group.PermissionLevel, result.Group.PermissionText, result.Group.CategoryName, result.Group.GroupCode, result.Group.LimitedMsgType, result.Group.Top, result.Group.HideGroupMembers, result.Group.DenyMembersUploadToGroupDisk));
                }
                else
                {
                    AppLogger.Log("GroupApi", string.Format("❌ GetInfoAsync 未能提取有效群数据, 长度={0}, HexPrefix={1}", bytes.Length, hexPrefix));
                    result.Code = -1;
                    result.Msg = "解析群信息失败 (长度=" + bytes.Length + ")";
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupApi", "❌ GetInfoAsync 异常: " + ex.ToString());
                result.Code = -1;
                result.Msg = "获取群详情失败: " + ex.Message;
            }
            return result;
        }

        public static async Task<GroupMembersResult> GetMembersAsync(string token, string groupId, int page = 1, int size = 50, string keywords = "")
        {
            var result = new GroupMembersResult();
            try
            {
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(groupId)) return Fail<GroupMembersResult>(result, "Token 或群聊 ID 为空");
                if (page < 1) page = 1; if (size < 1) size = 50;
                var request = new Yh.ListMemberRequest { GroupId = groupId, Keywords = keywords ?? "", DataField = new Yh.ListMemberRequest.Data { Page = page, Size = size } };
                var response = Yh.ListMemberResponse.Deserialize(await HttpHelper.PostProtobufAsync("/v1/group/list-member", Yh.ListMemberRequest.SerializeToBytes(request), token));
                if (response.Status != null) { result.Code = response.Status.Code; result.Msg = response.Status.Msg; }
                result.Total = response.Total;
                if (response.UserField != null) foreach (var item in response.UserField) if (item != null && item.UserInfoField != null)
                    result.Members.Add(new GroupMemberModel { UserId = item.UserInfoField.UserId, Name = item.UserInfoField.Name, AvatarUrl = item.UserInfoField.AvatarUrl, PermissionLevel = item.PermissionLevel, GagTimestamp = item.GagTimestamp, IsGag = item.IsGag });
            }
            catch (Exception ex) { result.Code = -1; result.Msg = "获取群成员失败: " + ex.Message; }
            return result;
        }

        public static Task<ApiResult> InviteAsync(string token, string groupId, string chatId, int chatType = 1) { return JsonAction(token, "/v1/group/invite", new Dictionary<string, object> { { "groupId", groupId }, { "chatId", chatId }, { "chatType", chatType } }); }
        public static Task<ApiResult> RemoveMemberAsync(string token, string groupId, string userId) { return JsonAction(token, "/v1/group/remove-member", new Dictionary<string, object> { { "groupId", groupId }, { "userId", userId } }); }
        public static Task<ApiResult> GagMemberAsync(string token, string groupId, string userId, int seconds) { return JsonAction(token, "/v1/group/gag-member", new Dictionary<string, object> { { "groupId", groupId }, { "userId", userId }, { "gag", seconds } }); }
        public static Task<ApiResult> SetMessageTypeLimitAsync(string token, string groupId, string type) { return JsonAction(token, "/v1/group/msg-type-limit", new Dictionary<string, object> { { "groupId", groupId }, { "type", type } }); }
        public static Task<ApiResult> SetMyNicknameAsync(string token, string groupId, string nickname) { return JsonAction(token, "/v1/group/edit-my-group-nickname", new Dictionary<string, object> { { "groupId", groupId }, { "nickname", nickname } }); }
        public static Task<ApiResult> SetKeywordAsync(string token, string groupId, string keyword) { return JsonAction(token, "/v1/group/edit-group-keyword", new Dictionary<string, object> { { "groupId", groupId }, { "keyword", keyword } }); }
        public static Task<ApiResult> SetAutoDeleteAsync(string token, string groupId, int days) { return JsonAction(token, "/v1/group/edit-auto-delete-message", new Dictionary<string, object> { { "groupId", groupId }, { "autoDeleteMessage", days } }); }
        public static Task<ApiResult> SetMemberUploadDisabledAsync(string token, string groupId, bool disabled) { return JsonAction(token, "/v1/group/edit-stop-member-upload-group-file", new Dictionary<string, object> { { "groupId", groupId }, { "stopMemberUploadGroupFile", disabled ? 1 : 0 } }); }
        public static Task<ApiResult> SwitchRecommendationAsync(string token, string groupId, bool hide) { return JsonAction(token, "/v1/group/switch", new Dictionary<string, object> { { "groupId", groupId }, { "hide", hide ? 1 : 0 } }); }
        public static Task<ApiResult> RemoveBotAsync(string token, string groupId, string botId) { return JsonAction(token, "/v1/group/remove-bot", new Dictionary<string, object> { { "groupId", groupId }, { "botId", botId } }); }
        public static Task<ApiResult> AgreeInviteAsync(string token, long id, int agree) { return JsonAction(token, "/v1/group/agree-invite", new Dictionary<string, object> { { "id", id }, { "agree", agree } }); }
        public static Task<ApiResult> MemberIsRemovedAsync(string token, string groupId, string userId) { return JsonAction(token, "/v1/group/member-is-removed", new Dictionary<string, object> { { "groupId", groupId }, { "userId", userId } }); }
        public static Task<ApiResult> SetShopEntryAsync(string token, string groupId, int entryPosition) { return JsonAction(token, "/v1/group/edit-shop-entry", new Dictionary<string, object> { { "groupId", groupId }, { "entryPosition", entryPosition } }); }
        public static async Task<string> GetLiveRoomsAsync(string token, string groupId) { return await HttpHelper.PostJsonAsync("/v1/group/live-room", JsonBody(new Dictionary<string, object> { { "groupId", groupId } }), token); }
        public static async Task<string> GetInstructionsAsync(string token, string groupId) { return await HttpHelper.PostJsonAsync("/v1/group/instruction-list", JsonBody(new Dictionary<string, object> { { "groupId", groupId } }), token); }
        public static async Task<string> GetCategoriesAsync(string token) { return await HttpHelper.GetAsync("/v1/group/category", token); }
        public static async Task<List<string>> GetCategoryNamesAsync(string token)
        {
            var list = new List<string>();
            var items = await GetCategoriesListAsync(token);
            foreach (var it in items) list.Add(it.DisplayName);
            return list;
        }

        public static async Task<List<GroupCategoryItem>> GetCategoriesListAsync(string token)
        {
            var list = new List<GroupCategoryItem>();
            try
            {
                string json = await GetCategoriesAsync(token);
                if (string.IsNullOrEmpty(json)) return list;

                JsonObject root;
                if (!JsonObject.TryParse(json, out root)) return list;

                if (root.ContainsKey("data") && root.GetNamedObject("data").ContainsKey("category"))
                {
                    var arr = root.GetNamedObject("data").GetNamedArray("category");
                    for (uint i = 0; i < arr.Count; i++)
                    {
                        var pObj = arr.GetObjectAt(i);
                        long pId = pObj.ContainsKey("id") ? (long)pObj.GetNamedNumber("id") : 0;
                        string pName = pObj.ContainsKey("name") ? pObj.GetNamedString("name") : "";

                        if (pObj.ContainsKey("subItems") && pObj.GetNamedValue("subItems").ValueType == JsonValueType.Array)
                        {
                            var subArr = pObj.GetNamedArray("subItems");
                            for (uint j = 0; j < subArr.Count; j++)
                            {
                                var sObj = subArr.GetObjectAt(j);
                                long sId = sObj.ContainsKey("id") ? (long)sObj.GetNamedNumber("id") : pId;
                                string sName = sObj.ContainsKey("name") ? sObj.GetNamedString("name") : "";
                                if (!string.IsNullOrEmpty(pName) && !string.IsNullOrEmpty(sName))
                                {
                                    list.Add(new GroupCategoryItem { Id = sId, Name = sName, DisplayName = string.Format("{0}-{1}", pName, sName) });
                                }
                                else if (!string.IsNullOrEmpty(sName))
                                {
                                    list.Add(new GroupCategoryItem { Id = sId, Name = sName, DisplayName = sName });
                                }
                            }
                        }
                        else if (!string.IsNullOrEmpty(pName))
                        {
                            list.Add(new GroupCategoryItem { Id = pId, Name = pName, DisplayName = pName });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupApi", "GetCategoriesListAsync exception: " + ex.Message);
            }
            return list;
        }
        public static async Task<string> GetRecommendedGroupsAsync(string token, long categoryId = 0, string keyword = "") { return await HttpHelper.PostJsonAsync("/v1/group/recommend/list", JsonBody(new Dictionary<string, object> { { "categoryId", categoryId }, { "keyword", keyword ?? "" } }), token); }

        public static async Task<ApiResult> EditAsync(string token, Yh.EditGroupRequest request)
        {
            var result = new ApiResult();
            try
            {
                AppLogger.Log("GroupApi", string.Format("EditAsync 请求: GroupId={0}, Name='{1}', Category='{2}'(Id={3}), Intro='{4}', Avatar='{5}', DirectJoin={6}, History={7}, Private={8}, HideMembers={9}",
                    request.GroupId, request.Name, request.CategoryName, request.CategoryId, request.Introduction, request.AvatarUrl, request.DirectJoin, request.HistoryMsg, request.Private, request.HideGroupMembers));
                var bytes = await HttpHelper.PostProtobufAsync("/v1/group/edit-group", Yh.EditGroupRequest.SerializeToBytes(request), token);
                if (bytes == null || bytes.Length == 0)
                {
                    result.Code = 1;
                    result.Msg = "success";
                    return result;
                }
                int code = 0;
                string msg = "";
                ParseEnvelopeStatus(bytes, out code, out msg);
                result.Code = code;
                result.Msg = msg;
                AppLogger.Log("GroupApi", string.Format("EditAsync 响应: Code={0}, Msg={1}", code, msg));
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupApi", "EditAsync 异常: " + ex.ToString());
                result.Code = -1;
                result.Msg = "编辑群信息失败: " + ex.Message;
            }
            return result;
        }

        public static async Task<ApiResult> CreateAsync(string token, string name, string introduction = "", string avatarUrl = "")
        {
            var result = new ApiResult();
            try { var response = Yh.CreateGroupResponse.Deserialize(await HttpHelper.PostProtobufAsync("/v1/group/create-group", Yh.CreateGroupRequest.SerializeToBytes(new Yh.CreateGroupRequest { Name = name, Introduction = introduction, AvatarUrl = avatarUrl }), token)); if (response.Status != null) { result.Code = response.Status.Code; result.Msg = response.Status.Msg; } }
            catch (Exception ex) { result.Code = -1; result.Msg = "创建群聊失败: " + ex.Message; }
            return result;
        }

        public static async Task<ApiResult> DismissAsync(string token, string groupId)
        {
            var result = new ApiResult();
            try { var response = Yh.StatusResponse.Deserialize(await HttpHelper.PostProtobufAsync("/v1/group/dismiss-group", Yh.DismissGroupRequest.SerializeToBytes(new Yh.DismissGroupRequest { GroupId = groupId }), token)); if (response.Status != null) { result.Code = response.Status.Code; result.Msg = response.Status.Msg; } }
            catch (Exception ex) { result.Code = -1; result.Msg = "解散群聊失败: " + ex.Message; }
            return result;
        }

        private static GroupInfoModel Map(Yh.GroupData x)
        {
            var m = new GroupInfoModel
            {
                GroupId = x.GroupId,
                Name = x.Name,
                AvatarUrl = x.AvatarUrl,
                AvatarId = (long)x.AvatarId,
                Introduction = x.Introduction,
                Headcount = (long)x.Headcount,
                Owner = x.Owner ?? x.CreateBy,
                PermissionLevel = (long)x.PermissonLevel,
                DirectJoin = x.DirectJoin,
                HistoryMsg = x.HistoryMsg,
                Private = x.Private,
                DoNotDisturb = x.DoNotDisturb,
                Top = x.Top,
                Recommendation = x.Recommandation,
                LimitedMsgType = x.LimitedMsgType,
                HideGroupMembers = x.HideGroupMembers,
                AutoDeleteMessage = x.AutoDeleteMessage ? 1L : 0L,
                DenyMembersUploadToGroupDisk = x.DenyMembersUploadToGroupDisk,
                MyGroupNickname = x.MyGroupNickname,
                GroupCode = x.GroupCode,
                CategoryName = x.CategoryName,
                CategoryId = (long)x.CategoryId,
                CommunityId = (long)x.CommunityId,
                CommunityName = x.CommunityName,
                UnbanTimestamp = x.BanUntilTimestamp,
                BanReason = x.BanReason
            };
            if (x.Admin != null) m.AdminIds.AddRange(x.Admin);
            return m;
        }

        private static bool LooksLikeDirectGroupData(byte[] bytes)
        {
            // field 1(string groupId), length, ASCII 群号；Status 的 field 1 后面通常是 8/16 等字段 key。
            return bytes != null && bytes.Length > 3 && bytes[0] == 10 && bytes[1] > 0 && bytes[1] < 64 && bytes[2] >= (byte)'0' && bytes[2] <= (byte)'9';
        }

        private static GroupInfoModel ParseGroupInfoSafely(byte[] bytes, out int statusCode, out string statusMsg)
        {
            statusCode = 0;
            statusMsg = "";
            if (bytes == null || bytes.Length == 0) return null;

            byte[] groupDataBytes = null;

            // 1. 扫描外层 Envelope 结构 (Field 1: Status, Field 2: GroupData)
            try
            {
                using (var ms = new MemoryStream(bytes))
                {
                    while (ms.Position < ms.Length)
                    {
                        ulong tag = ProtobufReader.ReadVarint(ms);
                        if (tag == 0) break;
                        int fieldNum = (int)(tag >> 3);
                        int wire = (int)(tag & 0x07);
                        if (fieldNum == 0) break;

                        if (fieldNum == 1 && wire == 2)
                        {
                            byte[] statusBytes = ProtobufReader.ReadBytes(ms);
                            ParseStatusSafely(statusBytes, out statusCode, out statusMsg);
                        }
                        else if (fieldNum == 2 && wire == 2)
                        {
                            groupDataBytes = ProtobufReader.ReadBytes(ms);
                        }
                        else
                        {
                            ProtobufReader.SkipField(ms, wire);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupApi", "ParseGroupInfoSafely envelope scan fallback: " + ex.Message);
            }

            // 如果未提取到内层 GroupData (例如直接返回 GroupData 结构)，则对根字节流解析
            if (groupDataBytes == null || groupDataBytes.Length == 0)
            {
                groupDataBytes = bytes;
            }

            // 2. 解析 GroupData 字段
            var model = new GroupInfoModel();
            using (var ms = new MemoryStream(groupDataBytes))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = 0;
                    try
                    {
                        tag = ProtobufReader.ReadVarint(ms);
                    }
                    catch { break; }

                    if (tag == 0) break;
                    int fieldNum = (int)(tag >> 3);
                    int wire = (int)(tag & 0x07);
                    if (fieldNum == 0) break;

                    try
                    {
                        switch (fieldNum)
                        {
                            case 1:
                                if (wire == 2) model.GroupId = ProtobufReader.ReadString(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 2:
                                if (wire == 2) model.Name = ProtobufReader.ReadString(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 3:
                                if (wire == 2) model.AvatarUrl = ProtobufReader.ReadString(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 4:
                                if (wire == 0) model.AvatarId = (long)ProtobufReader.ReadVarint(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 5:
                                if (wire == 2) model.Introduction = ProtobufReader.ReadString(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 6:
                                if (wire == 0) model.Headcount = (long)ProtobufReader.ReadVarint(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 7:
                                if (wire == 2)
                                {
                                    string cb = ProtobufReader.ReadString(ms);
                                    if (string.IsNullOrEmpty(model.Owner)) model.Owner = cb;
                                }
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 8:
                                if (wire == 0) model.DirectJoin = ProtobufReader.ReadVarint(ms) != 0;
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 9:
                                if (wire == 0) model.PermissionLevel = (long)ProtobufReader.ReadVarint(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 10:
                                if (wire == 0) model.HistoryMsg = ProtobufReader.ReadVarint(ms) != 0;
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 11:
                                if (wire == 2) model.CategoryName = ProtobufReader.ReadString(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 12:
                                if (wire == 0) model.CategoryId = (long)ProtobufReader.ReadVarint(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 13:
                                if (wire == 0) model.Private = ProtobufReader.ReadVarint(ms) != 0;
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 14:
                                if (wire == 0) model.DoNotDisturb = ProtobufReader.ReadVarint(ms) != 0;
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 15:
                                if (wire == 0) model.CommunityId = (long)ProtobufReader.ReadVarint(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 16:
                                if (wire == 2) model.CommunityName = ProtobufReader.ReadString(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 18:
                                if (wire == 0) model.UnbanTimestamp = (long)ProtobufReader.ReadVarint(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 19:
                                if (wire == 0) model.Top = ProtobufReader.ReadVarint(ms) != 0;
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 20:
                                if (wire == 2) model.AdminIds.Add(ProtobufReader.ReadString(ms));
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 22:
                                if (wire == 2) model.LimitedMsgType = ProtobufReader.ReadString(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 23:
                                if (wire == 2)
                                {
                                    string ow = ProtobufReader.ReadString(ms);
                                    if (!string.IsNullOrEmpty(ow)) model.Owner = ow;
                                }
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 24:
                                if (wire == 0) model.Recommendation = ProtobufReader.ReadVarint(ms) != 0;
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 28:
                                if (wire == 2) model.MyGroupNickname = ProtobufReader.ReadString(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 29:
                                if (wire == 2) model.GroupCode = ProtobufReader.ReadString(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 30:
                                if (wire == 0) model.HideGroupMembers = ProtobufReader.ReadVarint(ms) != 0;
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 32:
                                if (wire == 0) model.AutoDeleteMessage = (long)ProtobufReader.ReadVarint(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 33:
                                if (wire == 0) model.DenyMembersUploadToGroupDisk = ProtobufReader.ReadVarint(ms) != 0;
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 34:
                                if (wire == 2) model.BanReason = ProtobufReader.ReadString(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            default:
                                ProtobufReader.SkipField(ms, wire);
                                break;
                        }
                    }
                    catch
                    {
                        break;
                    }
                }
            }

            return string.IsNullOrEmpty(model.GroupId) && string.IsNullOrEmpty(model.Name) ? null : model;
        }

        private static void ParseStatusSafely(byte[] bytes, out int code, out string msg)
        {
            code = 0;
            msg = "";
            if (bytes == null || bytes.Length == 0) return;
            try
            {
                using (var ms = new MemoryStream(bytes))
                {
                    while (ms.Position < ms.Length)
                    {
                        ulong tag = ProtobufReader.ReadVarint(ms);
                        if (tag == 0) break;
                        int fieldNum = (int)(tag >> 3);
                        int wire = (int)(tag & 0x07);
                        if (fieldNum == 0) break;

                        switch (fieldNum)
                        {
                            case 2: // code
                                if (wire == 0) code = (int)ProtobufReader.ReadVarint(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            case 3: // msg
                                if (wire == 2) msg = ProtobufReader.ReadString(ms);
                                else ProtobufReader.SkipField(ms, wire);
                                break;
                            default:
                                ProtobufReader.SkipField(ms, wire);
                                break;
                        }
                    }
                }
            }
            catch { }
        }
        private static void ParseEnvelopeStatus(byte[] bytes, out int code, out string msg)
        {
            code = 0;
            msg = "";
            if (bytes == null || bytes.Length == 0) return;
            try
            {
                using (var ms = new MemoryStream(bytes))
                {
                    while (ms.Position < ms.Length)
                    {
                        ulong tag = ProtobufReader.ReadVarint(ms);
                        if (tag == 0) break;
                        int fieldNum = (int)(tag >> 3);
                        int wire = (int)(tag & 0x07);
                        if (fieldNum == 0) break;

                        if (fieldNum == 1 && wire == 2)
                        {
                            byte[] statusBytes = ProtobufReader.ReadBytes(ms);
                            ParseStatusSafely(statusBytes, out code, out msg);
                            if (code != 0 || !string.IsNullOrEmpty(msg)) return;
                        }
                        else if (fieldNum == 2 && wire == 0)
                        {
                            code = (int)ProtobufReader.ReadVarint(ms);
                        }
                        else if (fieldNum == 3 && wire == 2)
                        {
                            msg = ProtobufReader.ReadString(ms);
                        }
                        else
                        {
                            ProtobufReader.SkipField(ms, wire);
                        }
                    }
                }
            }
            catch { }
            if (code == 0 && string.IsNullOrEmpty(msg))
            {
                code = 1;
                msg = "success";
            }
        }

        public static Task<ApiResult> SetAdminAsync(string token, string groupId, string userId, bool enabled) { return JsonAction(token, "/v1/group/edit-admin", new Dictionary<string, object> { { "groupId", groupId }, { "userId", userId }, { "admin", enabled ? 1 : 0 } }); }
        public static Task<ApiResult> SetConversationSettingAsync(string token, string groupId, string type, bool enabled) { return type == "top" ? JsonAction(token, enabled ? "/v1/sticky/add" : "/v1/sticky/delete", new Dictionary<string, object> { { "chatId", groupId }, { "chatType", 2 } }) : JsonAction(token, "/v1/friend/no-notify", new Dictionary<string, object> { { "chatId", groupId }, { "noNotify", enabled ? 1 : 0 } }); }
        public static Task<ApiResult> LeaveAsync(string token, string groupId) { return JsonAction(token, "/v1/friend/delete-friend", new Dictionary<string, object> { { "chatId", groupId }, { "chatType", 2 } }); }
        private static T Fail<T>(T result, string message) where T : ApiResult { result.Code = -1; result.Msg = message; return result; }
        private static string JsonBody(Dictionary<string, object> values) { var obj = new JsonObject(); foreach (var pair in values) { if (pair.Value is string) obj.SetNamedValue(pair.Key, JsonValue.CreateStringValue((string)pair.Value)); else if (pair.Value is bool) obj.SetNamedValue(pair.Key, JsonValue.CreateBooleanValue((bool)pair.Value)); else obj.SetNamedValue(pair.Key, JsonValue.CreateNumberValue(Convert.ToDouble(pair.Value))); } return obj.Stringify(); }
        private static async Task<ApiResult> JsonAction(string token, string path, Dictionary<string, object> values)
        {
            var result = new ApiResult();
            try
            {
                var obj = new JsonObject();
                foreach (var pair in values)
                {
                    if (pair.Value is string) obj.SetNamedValue(pair.Key, JsonValue.CreateStringValue((string)pair.Value));
                    else if (pair.Value is bool) obj.SetNamedValue(pair.Key, JsonValue.CreateBooleanValue((bool)pair.Value));
                    else obj.SetNamedValue(pair.Key, JsonValue.CreateNumberValue(Convert.ToDouble(pair.Value)));
                }
                string body = obj.Stringify();
                AppLogger.Log("GroupApi", string.Format("JsonAction 请求: Path={0}, Body={1}", path, body));
                var text = await HttpHelper.PostJsonAsync(path, body, token);
                AppLogger.Log("GroupApi", string.Format("JsonAction 响应: Path={0}, Response={1}", path, text));
                if (string.IsNullOrEmpty(text))
                {
                    result.Code = -1;
                    result.Msg = "服务器无响应";
                    return result;
                }
                JsonObject root;
                if (JsonObject.TryParse(text, out root))
                {
                    if (root.ContainsKey("code"))
                    {
                        var codeVal = root.GetNamedValue("code");
                        if (codeVal.ValueType == JsonValueType.Number) result.Code = (int)codeVal.GetNumber();
                        else if (codeVal.ValueType == JsonValueType.String)
                        {
                            int c;
                            if (int.TryParse(codeVal.GetString(), out c)) result.Code = c;
                        }
                    }
                    else
                    {
                        result.Code = 1;
                    }
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                }
                else
                {
                    result.Code = 1;
                    result.Msg = text;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupApi", string.Format("JsonAction 异常: Path={0}, Ex={1}", path, ex.ToString()));
                result.Code = -1;
                result.Msg = ex.Message;
            }
            return result;
        }
    }
}
