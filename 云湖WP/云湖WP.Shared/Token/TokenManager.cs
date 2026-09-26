using System;
using System.Threading.Tasks;
using Windows.Storage;

namespace 云湖WP.Token
{
    /// <summary>
    /// 云湖用户 Token 与安全认证管理
    /// </summary>
    public static class TokenManager
    {
        private const string SettingKeyTokenEncrypted = "Token_Encrypted";
        private const string SettingKeyUserAccount = "Token_UserAccount";
        private const string SettingKeyLoginTimestamp = "Token_LoginTimestamp";

        // 内存缓存
        private static string _cachedToken = null;
        private static string _cachedAccount = null;

        /// <summary>
        /// 检查本地是否保存了 Token
        /// </summary>
        public static bool HasToken()
        {
            if (!string.IsNullOrEmpty(_cachedToken)) return true;

            var localSettings = ApplicationData.Current.LocalSettings;
            return localSettings.Values.ContainsKey(SettingKeyTokenEncrypted)
                || localSettings.Values.ContainsKey("UserToken")
                || localSettings.Values.ContainsKey("user_token")
                || localSettings.Values.ContainsKey("token");
        }

        /// <summary>
        /// 保存 Token 及账号
        /// </summary>
        public static async Task SaveTokenAsync(string token, string account = "")
        {
            if (string.IsNullOrEmpty(token)) return;

            _cachedToken = token;
            _cachedAccount = account;

            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                string encryptedToken = await SecurityHelper.EncryptStringAsync(token);
                localSettings.Values[SettingKeyTokenEncrypted] = encryptedToken;
                localSettings.Values["UserToken"] = token;
                
                if (!string.IsNullOrEmpty(account))
                {
                    localSettings.Values[SettingKeyUserAccount] = account;
                }

                localSettings.Values[SettingKeyLoginTimestamp] = DateTime.UtcNow.Ticks;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("TokenManager.SaveTokenAsync failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 获取当前保存的 Token (自动解密与多键兼容)
        /// </summary>
        public static async Task<string> GetTokenAsync()
        {
            if (!string.IsNullOrEmpty(_cachedToken)) return _cachedToken;

            var localSettings = ApplicationData.Current.LocalSettings;
            if (localSettings.Values.ContainsKey(SettingKeyTokenEncrypted))
            {
                string cipher = localSettings.Values[SettingKeyTokenEncrypted] as string;
                if (!string.IsNullOrEmpty(cipher))
                {
                    try
                    {
                        string decrypted = await SecurityHelper.DecryptStringAsync(cipher);
                        if (!string.IsNullOrEmpty(decrypted))
                        {
                            _cachedToken = decrypted;
                            return _cachedToken;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("TokenManager.GetTokenAsync failed: " + ex.Message);
                    }
                }
            }

            if (localSettings.Values.ContainsKey("UserToken"))
            {
                string raw = localSettings.Values["UserToken"] as string;
                if (!string.IsNullOrEmpty(raw))
                {
                    _cachedToken = raw;
                    return _cachedToken;
                }
            }

            if (localSettings.Values.ContainsKey("user_token"))
            {
                string raw = localSettings.Values["user_token"] as string;
                if (!string.IsNullOrEmpty(raw))
                {
                    _cachedToken = raw;
                    return _cachedToken;
                }
            }

            return "";
        }

        /// <summary>
        /// 获取保存的账号信息
        /// </summary>
        public static string GetSavedAccount()
        {
            if (!string.IsNullOrEmpty(_cachedAccount)) return _cachedAccount;

            var localSettings = ApplicationData.Current.LocalSettings;
            if (localSettings.Values.ContainsKey(SettingKeyUserAccount))
            {
                _cachedAccount = localSettings.Values[SettingKeyUserAccount] as string;
                return _cachedAccount;
            }

            return "";
        }

        /// <summary>
        /// 清除本地保存的 Token 和账号数据
        /// </summary>
        public static void ClearToken()
        {
            _cachedToken = null;
            _cachedAccount = null;

            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values.Remove(SettingKeyTokenEncrypted);
                localSettings.Values.Remove(SettingKeyUserAccount);
                localSettings.Values.Remove(SettingKeyLoginTimestamp);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("TokenManager.ClearToken failed: " + ex.Message);
            }
        }
    }
}
