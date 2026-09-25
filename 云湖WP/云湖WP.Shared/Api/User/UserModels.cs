using System;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.User
{
    /// <summary>
    /// 图形验证码结果
    /// </summary>
    public class CaptchaResult : ApiResult
    {
        public string Id { get; set; }
        public string B64s { get; set; }
    }

    /// <summary>
    /// 登录结果
    /// </summary>
    public class LoginResult : ApiResult
    {
        public string Token { get; set; }
    }

    /// <summary>
    /// 用户简要信息结果 (v1/user/info)
    /// </summary>
    public class UserInfoResult : ApiResult
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string AvatarUrl { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public double Coin { get; set; }
        public bool IsVip { get; set; }
    }

    /// <summary>
    /// 用户详细资料实体 (v1/user/get-user-data)
    /// </summary>
    public class UserProfileData
    {
        public long Id { get; set; }
        public string UserId { get; set; }
        public long LastLoginTime { get; set; }
        public long UpdateTime { get; set; }
        public string Introduction { get; set; }
        public int Gender { get; set; } // 1-男，2-女，3-其他
        public long Birthday { get; set; }
        public string Province { get; set; }
        public string City { get; set; }
        public string District { get; set; }
        public string LocationCode { get; set; }
    }

    /// <summary>
    /// 用户详细资料结果
    /// </summary>
    public class UserDataResult : ApiResult
    {
        public UserProfileData Data { get; set; }
    }
}
