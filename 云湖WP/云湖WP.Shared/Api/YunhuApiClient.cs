using System;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media.Imaging;
using 云湖WP.Api.Bot;
using 云湖WP.Api.Common;
using 云湖WP.Api.Conversation;
using 云湖WP.Api.Message;
using 云湖WP.Api.User;
using 云湖WP.Token;
using 云湖WP.Utils;

namespace 云湖WP
{

    /// <summary>
    /// 云湖 API 统一调用入口与外观门面 (Facade)
    /// </summary>
    public static class YunhuApiClient
    {
        public static string BaseUrl
        {
            get { return YunhuApiConfig.BaseUrl; }
        }

        public static string Platform
        {
            get { return YunhuApiConfig.Platform; }
        }

        /// <summary>
        /// 获取设备唯一 ID
        /// </summary>
        public static string GetDeviceId()
        {
            return YunhuApiConfig.GetDeviceId();
        }

        #region User 路由直接代理 (/v1/user & /v1/verification)

        public static async Task<云湖WP.Api.User.CaptchaResult> GetCaptchaAsync()
        {
            return await UserApi.GetCaptchaAsync();
        }

        public static async Task<云湖WP.Api.Common.ApiResult> GetSmsVerificationCodeAsync(string mobile, string code, string captchaId)
        {
            return await UserApi.GetSmsVerificationCodeAsync(mobile, code, captchaId);
        }

        public static async Task<云湖WP.Api.Common.ApiResult> GetEmailVerificationCodeAsync(string email, string code, string captchaId, string typ = "")
        {
            return await UserApi.GetEmailVerificationCodeAsync(email, code, captchaId, typ);
        }

        public static async Task<云湖WP.Api.User.LoginResult> PhoneVerificationLoginAsync(string mobile, string captchaSmsCode)
        {
            return await UserApi.PhoneVerificationLoginAsync(mobile, captchaSmsCode);
        }

        public static async Task<云湖WP.Api.User.LoginResult> EmailLoginAsync(string email, string password)
        {
            return await UserApi.EmailLoginAsync(email, password);
        }

        public static async Task<云湖WP.Api.User.UserInfoResult> GetUserInfoAsync(string token)
        {
            return await UserApi.GetUserInfoAsync(token);
        }

        public static async Task<云湖WP.Api.Common.ApiResult> LogoutAsync(string token)
        {
            return await UserApi.LogoutAsync(token);
        }

        #endregion

        #region Conversation 路由直接代理 (/v1/conversation & /v1/sticky)

        public static async Task<云湖WP.Api.Conversation.ConversationListResult> GetConversationListAsync(string token, string md5 = "")
        {
            return await ConversationApi.GetConversationListAsync(token, md5);
        }

        public static async Task<云湖WP.Api.Conversation.StickyListResult> GetStickyListAsync(string token)
        {
            return await ConversationApi.GetStickyListAsync(token);
        }

        #endregion

        #region Message 路由直接代理 (/v1/msg)

        public static async Task<云湖WP.Api.Message.MessageListResult> GetMessageListBySeqAsync(string token, string chatId, int chatType, long msgSeq = 0)
        {
            return await MessageApi.GetMessageListBySeqAsync(token, chatId, chatType, msgSeq);
        }

        public static async Task<云湖WP.Api.Message.SendMessageResult> SendTextMessageAsync(string token, string chatId, int chatType, string text)
        {
            return await MessageApi.SendTextMessageAsync(token, chatId, chatType, text);
        }

        #endregion

        #region Common 工具方法

        public static async Task<BitmapImage> Base64ToBitmapImageAsync(string base64)
        {
            return await ImageHelper.Base64ToBitmapImageAsync(base64);
        }

        #endregion
    }
}
