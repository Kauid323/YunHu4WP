using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage.Streams;

namespace 云湖WP.Token
{
    /// <summary>
    /// WinRT 原生数据保护与加密辅助类 (针对 Windows Phone 8.1 / WinRT DPAPI)
    /// </summary>
    public static class SecurityHelper
    {
        private const string Descriptor = "LOCAL=user";

        /// <summary>
        /// 加密明文字符串
        /// </summary>
        public static async Task<string> EncryptStringAsync(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";

            try
            {
                var provider = new DataProtectionProvider(Descriptor);
                IBuffer buffMsg = CryptographicBuffer.ConvertStringToBinary(plainText, BinaryStringEncoding.Utf8);
                IBuffer buffProtected = await provider.ProtectAsync(buffMsg);
                return CryptographicBuffer.EncodeToBase64String(buffProtected);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SecurityHelper.EncryptStringAsync Exception: " + ex.Message);
                return plainText; // 降级返回
            }
        }

        /// <summary>
        /// 解密密文字符串
        /// </summary>
        public static async Task<string> DecryptStringAsync(string cipherBase64)
        {
            if (string.IsNullOrEmpty(cipherBase64)) return "";

            try
            {
                var provider = new DataProtectionProvider();
                IBuffer buffProtected = CryptographicBuffer.DecodeFromBase64String(cipherBase64);
                IBuffer buffUnprotected = await provider.UnprotectAsync(buffProtected);
                return CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, buffUnprotected);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SecurityHelper.DecryptStringAsync Exception: " + ex.Message);
                return cipherBase64; // 降级返回
            }
        }
    }
}
