using System;
using Windows.Storage;

namespace 云湖WP.Api.Common
{
    /// <summary>
    /// 云湖 API 全局配置
    /// </summary>
    public static class YunhuApiConfig
    {
        public const string BaseUrl = "https://chat-go.jwzhd.com";
        public const string Platform = "Windows Phone";

        private const string SettingKeyDeviceId = "AppDeviceId";

        /// <summary>
        /// 获取当前设备唯一标识符
        /// </summary>
        public static string GetDeviceId()
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.ContainsKey(SettingKeyDeviceId))
            {
                string id = settings.Values[SettingKeyDeviceId] as string;
                if (!string.IsNullOrEmpty(id)) return id;
            }

            string newId = Guid.NewGuid().ToString("N");
            settings.Values[SettingKeyDeviceId] = newId;
            return newId;
        }
    }
}
