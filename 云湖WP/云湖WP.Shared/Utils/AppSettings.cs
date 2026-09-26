using System;
using Windows.Storage;

namespace 云湖WP.Utils
{
    /// <summary>
    /// 全局应用设置管理器
    /// </summary>
    public static class AppSettings
    {
        private const string KEY_DEFAULT_STARTUP_PAGE = "Settings_DefaultStartupPage";

        /// <summary>
        /// 默认启动页面索引 (0: 消息, 1: 联系人, 2: 动态, 3: 我)
        /// </summary>
        public static int DefaultStartupPageIndex
        {
            get
            {
                try
                {
                    var values = ApplicationData.Current.LocalSettings.Values;
                    if (values.ContainsKey(KEY_DEFAULT_STARTUP_PAGE))
                    {
                        int val = Convert.ToInt32(values[KEY_DEFAULT_STARTUP_PAGE]);
                        if (val >= 0 && val <= 3) return val;
                    }
                }
                catch { }
                return 0; // 默认启动页：消息
            }
            set
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values[KEY_DEFAULT_STARTUP_PAGE] = value;
                }
                catch { }
            }
        }
    }
}
