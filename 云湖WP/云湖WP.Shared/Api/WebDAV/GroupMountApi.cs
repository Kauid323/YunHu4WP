using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;
using 云湖WP.Utils;

namespace 云湖WP.Api.WebDAV
{
    /// <summary>
    /// 群 WebDAV 挂载设置 API (/v1/mount-setting)
    /// </summary>
    public static class GroupMountApi
    {
        public static async Task<List<WebDAVMountSetting>> GetMountListAsync(string token, string groupId)
        {
            var list = new List<WebDAVMountSetting>();
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(groupId)) return list;

            try
            {
                // 1. 获取公钥并准备 RSA 密钥与 IV 参数
                byte[] pubKey = await WebDAVEncryption.GetPublicKeyBytesAsync();
                var encParams = WebDAVEncryption.PrepareEncryptionParams(pubKey);
                if (encParams == null)
                {
                    AppLogger.Log("GroupMountApi", "加密参数生成失败");
                    return list;
                }

                // 2. 调用 POST /v1/mount-setting/list
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("groupId", JsonValue.CreateStringValue(groupId));
                reqObj.SetNamedValue("encryptKey", JsonValue.CreateStringValue(encParams.EncryptKeyBase64));
                reqObj.SetNamedValue("encryptIv", JsonValue.CreateStringValue(encParams.EncryptIvBase64));

                string respJson = await HttpHelper.PostJsonAsync("/v1/mount-setting/list", reqObj.Stringify(), token);
                JsonObject root;
                if (JsonObject.TryParse(respJson, out root))
                {
                    if (root.ContainsKey("data") && root.GetNamedValue("data").ValueType == JsonValueType.Object)
                    {
                        var data = root.GetNamedObject("data");
                        if (data.ContainsKey("list") && data.GetNamedValue("list").ValueType == JsonValueType.Array)
                        {
                            var arr = data.GetNamedArray("list");
                            for (uint i = 0; i < arr.Count; i++)
                            {
                                if (arr[(int)i].ValueType != JsonValueType.Object) continue;
                                var item = arr.GetObjectAt(i);
                                var mount = new WebDAVMountSetting
                                {
                                    Id = item.ContainsKey("id") ? (long)item.GetNamedNumber("id") : 0,
                                    GroupId = item.ContainsKey("groupId") ? item.GetNamedString("groupId") : groupId,
                                    MountName = item.ContainsKey("mountName") ? item.GetNamedString("mountName") : "挂载点",
                                    WebdavUrl = item.ContainsKey("webdavUrl") ? item.GetNamedString("webdavUrl") : "",
                                    WebdavUserName = item.ContainsKey("webdavUserName") ? item.GetNamedString("webdavUserName") : "",
                                    EncryptedPassword = item.ContainsKey("webdavPassword") ? item.GetNamedString("webdavPassword") : "",
                                    WebdavRootPath = item.ContainsKey("webdavRootPath") ? item.GetNamedString("webdavRootPath") : "",
                                    CreateTime = item.ContainsKey("createTime") ? (long)item.GetNamedNumber("createTime") : 0,
                                    UserId = item.ContainsKey("userId") ? item.GetNamedString("userId") : ""
                                };

                                // 3. 解密密码
                                if (!string.IsNullOrEmpty(mount.EncryptedPassword))
                                {
                                    mount.WebdavPassword = WebDAVEncryption.DecryptWebDAVPassword(mount.EncryptedPassword, encParams.RawKey, encParams.RawIv);
                                }
                                else
                                {
                                    mount.WebdavPassword = "";
                                }

                                list.Add(mount);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupMountApi", "GetMountListAsync 异常: " + ex.Message);
            }

            return list;
        }

        public static async Task<ApiResult> CreateMountAsync(string token, string groupId, string mountName, string url, string user, string pwd, string rootPath)
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("groupId", JsonValue.CreateStringValue(groupId));
                reqObj.SetNamedValue("mountName", JsonValue.CreateStringValue(mountName ?? ""));
                reqObj.SetNamedValue("webdavUrl", JsonValue.CreateStringValue(url ?? ""));
                reqObj.SetNamedValue("webdavUserName", JsonValue.CreateStringValue(user ?? ""));
                reqObj.SetNamedValue("webdavPassword", JsonValue.CreateStringValue(pwd ?? ""));
                reqObj.SetNamedValue("webdavRootPath", JsonValue.CreateStringValue(rootPath ?? ""));

                string resp = await HttpHelper.PostJsonAsync("/v1/mount-setting/create", reqObj.Stringify(), token);
                JsonObject root;
                if (JsonObject.TryParse(resp, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "添加 WebDAV 失败: " + ex.Message;
            }
            return result;
        }

        public static async Task<ApiResult> DeleteMountAsync(string token, long id)
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("id", JsonValue.CreateNumberValue(id));

                string resp = await HttpHelper.PostJsonAsync("/v1/mount-setting/delete", reqObj.Stringify(), token);
                JsonObject root;
                if (JsonObject.TryParse(resp, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "删除 WebDAV 失败: " + ex.Message;
            }
            return result;
        }
    }
}
