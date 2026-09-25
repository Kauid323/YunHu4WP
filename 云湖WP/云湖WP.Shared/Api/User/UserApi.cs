using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using 云湖WP.Api.Common;
using 云湖WP.Utils;

namespace 云湖WP.Api.User
{
    /// <summary>
    /// 用户与验证码相关 API (/v1/user 与 /v1/verification)
    /// </summary>
    public static class UserApi
    {
        /// <summary>
        /// 获取图形人机验证码 (POST /v1/user/captcha)
        /// </summary>
        public static async Task<CaptchaResult> GetCaptchaAsync()
        {
            var result = new CaptchaResult();
            try
            {
                string jsonStr = await HttpHelper.PostJsonAsync("/v1/user/captcha", "{}");
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                    if (root.ContainsKey("data") && root.GetNamedValue("data").ValueType == JsonValueType.Object)
                    {
                        var data = root.GetNamedObject("data");
                        if (data.ContainsKey("id")) result.Id = data.GetNamedString("id");
                        if (data.ContainsKey("b64s")) result.B64s = data.GetNamedString("b64s");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "网络连接异常: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 获取短信验证码 (POST /v1/verification/get-verification-code)
        /// </summary>
        public static async Task<ApiResult> GetSmsVerificationCodeAsync(string mobile, string code, string captchaId)
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("mobile", JsonValue.CreateStringValue(mobile));
                reqObj.SetNamedValue("code", JsonValue.CreateStringValue(code));
                reqObj.SetNamedValue("id", JsonValue.CreateStringValue(captchaId));
                reqObj.SetNamedValue("platform", JsonValue.CreateStringValue(YunhuApiConfig.Platform));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/verification/get-verification-code", reqObj.Stringify());
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "网络请求失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 获取邮箱验证码 (POST /v1/verification/get-email-verification-code)
        /// </summary>
        public static async Task<ApiResult> GetEmailVerificationCodeAsync(string email, string code, string captchaId, string typ = "")
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("email", JsonValue.CreateStringValue(email));
                reqObj.SetNamedValue("code", JsonValue.CreateStringValue(code));
                reqObj.SetNamedValue("id", JsonValue.CreateStringValue(captchaId));
                if (!string.IsNullOrEmpty(typ))
                {
                    reqObj.SetNamedValue("typ", JsonValue.CreateStringValue(typ));
                }

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/verification/get-email-verification-code", reqObj.Stringify());
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "网络请求失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 手机号验证码登录 (POST /v1/user/verification-login)
        /// </summary>
        public static async Task<LoginResult> PhoneVerificationLoginAsync(string mobile, string captchaSmsCode)
        {
            var result = new LoginResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("mobile", JsonValue.CreateStringValue(mobile));
                reqObj.SetNamedValue("captcha", JsonValue.CreateStringValue(captchaSmsCode));
                reqObj.SetNamedValue("deviceId", JsonValue.CreateStringValue(YunhuApiConfig.GetDeviceId()));
                reqObj.SetNamedValue("platform", JsonValue.CreateStringValue(YunhuApiConfig.Platform));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/user/verification-login", reqObj.Stringify());
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                    if (root.ContainsKey("data") && root.GetNamedValue("data").ValueType == JsonValueType.Object)
                    {
                        var data = root.GetNamedObject("data");
                        if (data.ContainsKey("token")) result.Token = data.GetNamedString("token");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "网络连接失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 邮箱密码登录 (POST /v1/user/email-login)
        /// </summary>
        public static async Task<LoginResult> EmailLoginAsync(string email, string password)
        {
            var result = new LoginResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("email", JsonValue.CreateStringValue(email));
                reqObj.SetNamedValue("password", JsonValue.CreateStringValue(password));
                reqObj.SetNamedValue("deviceId", JsonValue.CreateStringValue(YunhuApiConfig.GetDeviceId()));
                reqObj.SetNamedValue("platform", JsonValue.CreateStringValue(YunhuApiConfig.Platform));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/user/email-login", reqObj.Stringify());
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                    if (root.ContainsKey("data") && root.GetNamedValue("data").ValueType == JsonValueType.Object)
                    {
                        var data = root.GetNamedObject("data");
                        if (data.ContainsKey("token")) result.Token = data.GetNamedString("token");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "网络连接失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 获取用户自身完整信息 (GET /v1/user/info，位于 Api/User/Self，支持 Protobuf 与 JSON 自动适配)
        /// </summary>
        public static async Task<云湖WP.Api.User.Self.UserSelfInfoResult> GetSelfInfoAsync(string token)
        {
            var result = new 云湖WP.Api.User.Self.UserSelfInfoResult();
            if (string.IsNullOrEmpty(token))
            {
                result.Code = -1;
                result.Msg = "Token 为空";
                return result;
            }

            try
            {
                // GET /v1/user/info (Protobuf 协议)
                byte[] bytes = await HttpHelper.GetProtobufAsync("/v1/user/info", token);
                if (bytes != null && bytes.Length > 0)
                {
                    result = 云湖WP.Api.User.Self.UserSelfProtobufCodec.DecodeSelfInfoResponse(bytes);
                    AppLogger.Log("UserApi", string.Format("GetSelfInfoAsync decoded: Code={0}, Name={1}, Id={2}, AvatarUrl={3}", result.Code, result.Name, result.Id, result.AvatarUrl));
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
                result.Msg = "获取用户自身信息失败: " + ex.Message;
                AppLogger.Log("UserApi", "GetSelfInfoAsync exception: " + ex.Message);
            }
            return result;
        }

        /// <summary>
        /// 获取用户自身简要信息 (GET /v1/user/info)
        /// </summary>
        public static async Task<UserInfoResult> GetUserInfoAsync(string token)
        {
            var result = new UserInfoResult();
            if (string.IsNullOrEmpty(token))
            {
                result.Code = -1;
                result.Msg = "Token 为空";
                return result;
            }

            try
            {
                var selfRes = await GetSelfInfoAsync(token);
                if (selfRes != null && selfRes.Data != null)
                {
                    result.Code = selfRes.Code;
                    result.Msg = selfRes.Msg;
                    result.Id = selfRes.Data.Id;
                    result.Name = selfRes.Data.Name;
                    result.AvatarUrl = selfRes.Data.AvatarUrl;
                    result.Phone = selfRes.Data.Phone;
                    result.Email = selfRes.Data.Email;
                    result.Coin = selfRes.Data.Coin;
                    result.IsVip = selfRes.Data.IsVip;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取用户信息失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 获取指定用户的详细资料 (POST /v1/user/get-user)
        /// </summary>
        public static async Task<云湖WP.Api.User.Info.UserDetailResult> GetUserDetailAsync(string token, string userId)
        {
            var result = new 云湖WP.Api.User.Info.UserDetailResult();
            if (string.IsNullOrEmpty(userId))
            {
                result.Code = -1;
                result.Msg = "用户ID不能为空";
                return result;
            }

            try
            {
                byte[] reqBytes = 云湖WP.Api.User.Info.UserInfoProtobufCodec.EncodeGetUserRequest(userId);
                byte[] respBytes = await HttpHelper.PostProtobufAsync("/v1/user/get-user", reqBytes, token);

                if (respBytes == null || respBytes.Length == 0)
                {
                    result.Code = -1;
                    result.Msg = "服务器返回空响应";
                    return result;
                }

                result = 云湖WP.Api.User.Info.UserInfoProtobufCodec.DecodeGetUserResponse(respBytes);
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取用户资料异常: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 获取自身个人详细资料 (POST /v1/user/get-user-data)
        /// </summary>
        public static async Task<UserDataResult> GetUserDataAsync(string token)
        {
            var result = new UserDataResult();
            try
            {
                string jsonStr = await HttpHelper.PostJsonAsync("/v1/user/get-user-data", "{}", token);
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                    if (root.ContainsKey("data") && root.GetNamedValue("data").ValueType == JsonValueType.Object)
                    {
                        var dataOuter = root.GetNamedObject("data");
                        if (dataOuter.ContainsKey("data") && dataOuter.GetNamedValue("data").ValueType == JsonValueType.Object)
                        {
                            var d = dataOuter.GetNamedObject("data");
                            var profile = new UserProfileData();
                            if (d.ContainsKey("id")) profile.Id = (long)d.GetNamedNumber("id");
                            if (d.ContainsKey("userId")) profile.UserId = d.GetNamedString("userId");
                            if (d.ContainsKey("lastLoginTime")) profile.LastLoginTime = (long)d.GetNamedNumber("lastLoginTime");
                            if (d.ContainsKey("update_time")) profile.UpdateTime = (long)d.GetNamedNumber("update_time");
                            if (d.ContainsKey("introduction")) profile.Introduction = d.GetNamedString("introduction");
                            if (d.ContainsKey("gender")) profile.Gender = (int)d.GetNamedNumber("gender");
                            if (d.ContainsKey("birthday")) profile.Birthday = (long)d.GetNamedNumber("birthday");
                            if (d.ContainsKey("province")) profile.Province = d.GetNamedString("province");
                            if (d.ContainsKey("city")) profile.City = d.GetNamedString("city");
                            if (d.ContainsKey("district")) profile.District = d.GetNamedString("district");
                            if (d.ContainsKey("locationCode")) profile.LocationCode = d.GetNamedString("locationCode");
                            result.Data = profile;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "获取详细资料失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 修改自身个人资料 (POST /v1/user/save-user-data，位于 Api/User/Edit)
        /// </summary>
        public static async Task<云湖WP.Api.User.Edit.UserEditResult> EditUserProfileAsync(string token, 云湖WP.Api.User.Edit.UserEditRequest req)
        {
            var result = new 云湖WP.Api.User.Edit.UserEditResult();
            if (req == null || string.IsNullOrEmpty(token))
            {
                result.Code = -1;
                result.Msg = "参数不能为空";
                return result;
            }

            try
            {
                string jsonStr = await HttpHelper.PostJsonAsync("/v1/user/save-user-data", req.ToJson(), token);
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "保存个人资料失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 修改自身个人资料 (POST /v1/user/save-user-data)
        /// </summary>
        public static async Task<ApiResult> SaveUserDataAsync(string token, string introduction, int gender, long birthday, string province, string city, string district, string locationCode)
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("introduction", JsonValue.CreateStringValue(introduction));
                reqObj.SetNamedValue("gender", JsonValue.CreateNumberValue(gender));
                reqObj.SetNamedValue("birthday", JsonValue.CreateNumberValue(birthday));
                reqObj.SetNamedValue("province", JsonValue.CreateStringValue(province));
                reqObj.SetNamedValue("city", JsonValue.CreateStringValue(city));
                reqObj.SetNamedValue("district", JsonValue.CreateStringValue(district));
                reqObj.SetNamedValue("locationCode", JsonValue.CreateStringValue(locationCode));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/user/save-user-data", reqObj.Stringify(), token);
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "保存个人资料失败: " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 退出登录 (POST /v1/user/logout)
        /// </summary>
        public static async Task<ApiResult> LogoutAsync(string token)
        {
            var result = new ApiResult();
            try
            {
                var reqObj = new JsonObject();
                reqObj.SetNamedValue("device-id", JsonValue.CreateStringValue(YunhuApiConfig.GetDeviceId()));

                string jsonStr = await HttpHelper.PostJsonAsync("/v1/user/logout", reqObj.Stringify(), token);
                JsonObject root;
                if (JsonObject.TryParse(jsonStr, out root))
                {
                    if (root.ContainsKey("code")) result.Code = (int)root.GetNamedNumber("code");
                    if (root.ContainsKey("msg")) result.Msg = root.GetNamedString("msg");
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Msg = "退出登录失败: " + ex.Message;
            }
            return result;
        }
    }
}
