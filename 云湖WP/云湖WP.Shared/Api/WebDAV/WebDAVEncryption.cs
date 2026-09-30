using System;
using System.Text;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;
using 云湖WP.Api.Common;
using 云湖WP.Utils;

namespace 云湖WP.Api.WebDAV
{
    /// <summary>
    /// WebDAV 密码解密与 RSA 密钥交换工具类 (WinRT 原生加解密)
    /// </summary>
    public static class WebDAVEncryption
    {
        private const string HardcodedPublicKeyPem = @"-----BEGIN RSA PUBLIC KEY-----
MIICCgKCAgEA5MSOx8O11qDYdmR40FUs3a0gjdEzQOfHJFVlSilg83sbl65D3alh
SDfP1h52dbr8m1XmQkjUaTCXfAGdN2p3M6wR6H7pniuHjSzXyPq7ZhmXxFa9dNeR
YDgePFVlLzBYEklYWa2YQ+bu2QRU3h2I94Go91vWVL9KEFe2fi1sfaycyU8h5DS5
D7f3SAtg1L2kcLU+2kfzF5XTyXJUlo0DkdV38BXq0gPqURiEscBRM5K5WF73xJfc
rUcPfDSp1OP8itTNPdgEUC8H1tEPnWhMC7vDNPxuGZ2dGXhedMuO9KW/QmdZ9qi1
5W+ZXdUQdKmTo/V8Z5gDxjWW3/LC6/PexS9HeIyuoLgYWd1GtOcl19FhvipM2Wuv
UjJvwUOqlyPa/MR8e5z5P2J4DEd74QSHaCuNHHDOZMuJWtNGcirXpzo0a41rwpfz
lo4SrzberFL1dl361OewJkqq4fg5dfGgGZcPTxZ+WxVWpmMSlimrpRNcZNy8+orn
iRRhVTW6cXvaku2HlSZGvI+7eoHIYaE0YcOzMzdODTKYl33FSbRRIn2ly0bfqoMd
192qmGAkPa7eqdI0FZSjHmRxc2DEXOq9A6BpJGq0zyVhoyGvfVc88qAh4gwGzvx/
yQGy3WJ+xqP1aUJardDi1g5VPLp0jQcg7k0QP98NfxhdOb2jiH0ClkcCAwEAAQ==
-----END RSA PUBLIC KEY-----";

        public class EncryptionParams
        {
            public string EncryptKeyBase64 { get; set; }
            public string EncryptIvBase64 { get; set; }
            public byte[] RawKey { get; set; }
            public byte[] RawIv { get; set; }
        }

        public static byte[] GenerateRandomBytes(int length)
        {
            var buffer = CryptographicBuffer.GenerateRandom((uint)length);
            byte[] bytes;
            CryptographicBuffer.CopyToByteArray(buffer, out bytes);
            return bytes;
        }

        public static async Task<byte[]> GetPublicKeyBytesAsync()
        {
            try
            {
                string pem = await HttpHelper.GetAsync("https://chat.yhchat.com/assets/key/apps_public.pem");
                if (!string.IsNullOrEmpty(pem) && pem.Contains("RSA PUBLIC KEY"))
                {
                    return Encoding.UTF8.GetBytes(pem);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("WebDAVEncryption", "下载公钥失败，使用内置公钥: " + ex.Message);
            }
            return Encoding.UTF8.GetBytes(HardcodedPublicKeyPem);
        }

        public static EncryptionParams PrepareEncryptionParams(byte[] publicKeyPemBytes = null)
        {
            try
            {
                byte[] rawKey = GenerateRandomBytes(16);
                byte[] rawIv = GenerateRandomBytes(16);

                string pem = publicKeyPemBytes != null ? Encoding.UTF8.GetString(publicKeyPemBytes, 0, publicKeyPemBytes.Length) : HardcodedPublicKeyPem;
                CryptographicKey rsaKey = ParsePublicKey(pem);
                if (rsaKey == null)
                {
                    AppLogger.Log("WebDAVEncryption", "解析 RSA 公钥失败");
                    return null;
                }

                var keyBuf = CryptographicBuffer.CreateFromByteArray(rawKey);
                var ivBuf = CryptographicBuffer.CreateFromByteArray(rawIv);

                var encKeyBuf = CryptographicEngine.Encrypt(rsaKey, keyBuf, null);
                var encIvBuf = CryptographicEngine.Encrypt(rsaKey, ivBuf, null);

                return new EncryptionParams
                {
                    EncryptKeyBase64 = CryptographicBuffer.EncodeToBase64String(encKeyBuf),
                    EncryptIvBase64 = CryptographicBuffer.EncodeToBase64String(encIvBuf),
                    RawKey = rawKey,
                    RawIv = rawIv
                };
            }
            catch (Exception ex)
            {
                AppLogger.Log("WebDAVEncryption", "PrepareEncryptionParams 异常: " + ex.ToString());
                return null;
            }
        }

        public static string DecryptWebDAVPassword(string encryptedPassword, byte[] keyBytes, byte[] ivBytes)
        {
            if (string.IsNullOrEmpty(encryptedPassword) || keyBytes == null || ivBytes == null)
                return "";

            try
            {
                var cipherBuffer = CryptographicBuffer.DecodeFromBase64String(encryptedPassword.Trim());
                var aesProvider = SymmetricKeyAlgorithmProvider.OpenAlgorithm(SymmetricAlgorithmNames.AesCbcPkcs7);
                var symmetricKey = aesProvider.CreateSymmetricKey(CryptographicBuffer.CreateFromByteArray(keyBytes));
                var decryptedBuffer = CryptographicEngine.Decrypt(symmetricKey, cipherBuffer, CryptographicBuffer.CreateFromByteArray(ivBytes));
                byte[] decryptedBytes;
                CryptographicBuffer.CopyToByteArray(decryptedBuffer, out decryptedBytes);
                return Encoding.UTF8.GetString(decryptedBytes, 0, decryptedBytes.Length);
            }
            catch (Exception ex)
            {
                AppLogger.Log("WebDAVEncryption", "解密 WebDAV 密码失败: " + ex.Message);
                return "";
            }
        }

        private static CryptographicKey ParsePublicKey(string pemContent)
        {
            try
            {
                string cleanContent = pemContent
                    .Replace("-----BEGIN RSA PUBLIC KEY-----", "")
                    .Replace("-----END RSA PUBLIC KEY-----", "")
                    .Replace("-----BEGIN PUBLIC KEY-----", "")
                    .Replace("-----END PUBLIC KEY-----", "")
                    .Replace("\r", "")
                    .Replace("\n", "")
                    .Replace(" ", "")
                    .Trim();

                byte[] keyBytes = Convert.FromBase64String(cleanContent);
                var provider = AsymmetricKeyAlgorithmProvider.OpenAlgorithm(AsymmetricAlgorithmNames.RsaPkcs1);

                // 尝试 PKCS#1 格式导入
                try
                {
                    var buf = CryptographicBuffer.CreateFromByteArray(keyBytes);
                    return provider.ImportPublicKey(buf, CryptographicPublicKeyBlobType.Pkcs1RsaPublicKey);
                }
                catch { }

                // 尝试转为 X.509 格式后导入
                try
                {
                    byte[] x509Bytes = ConvertPkcs1ToX509(keyBytes);
                    var x509Buf = CryptographicBuffer.CreateFromByteArray(x509Bytes);
                    return provider.ImportPublicKey(x509Buf, CryptographicPublicKeyBlobType.X509SubjectPublicKeyInfo);
                }
                catch { }

                return null;
            }
            catch (Exception ex)
            {
                AppLogger.Log("WebDAVEncryption", "ParsePublicKey 异常: " + ex.Message);
                return null;
            }
        }

        private static byte[] ConvertPkcs1ToX509(byte[] pkcs1Bytes)
        {
            byte[] rsaOidSequence = new byte[] {
                0x30, 0x0d,
                0x06, 0x09,
                0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x01,
                0x05, 0x00
            };
            int bitStringLength = pkcs1Bytes.Length + 1;
            byte[] bitStringLengthBytes = EncodeDerLength(bitStringLength);
            int totalContentLength = rsaOidSequence.Length + 1 + bitStringLengthBytes.Length + bitStringLength;
            byte[] totalLengthBytes = EncodeDerLength(totalContentLength);

            byte[] x509Bytes = new byte[1 + totalLengthBytes.Length + totalContentLength];
            int offset = 0;
            x509Bytes[offset++] = 0x30;
            System.Buffer.BlockCopy(totalLengthBytes, 0, x509Bytes, offset, totalLengthBytes.Length);
            offset += totalLengthBytes.Length;
            System.Buffer.BlockCopy(rsaOidSequence, 0, x509Bytes, offset, rsaOidSequence.Length);
            offset += rsaOidSequence.Length;
            x509Bytes[offset++] = 0x03;
            System.Buffer.BlockCopy(bitStringLengthBytes, 0, x509Bytes, offset, bitStringLengthBytes.Length);
            offset += bitStringLengthBytes.Length;
            x509Bytes[offset++] = 0x00;
            System.Buffer.BlockCopy(pkcs1Bytes, 0, x509Bytes, offset, pkcs1Bytes.Length);
            return x509Bytes;
        }

        private static byte[] EncodeDerLength(int length)
        {
            if (length < 128)
            {
                return new byte[] { (byte)length };
            }
            else if (length < 256)
            {
                return new byte[] { 0x81, (byte)length };
            }
            else if (length < 65536)
            {
                return new byte[] { 0x82, (byte)(length >> 8), (byte)(length & 0xFF) };
            }
            else
            {
                return new byte[] { 0x83, (byte)(length >> 16), (byte)((length >> 8) & 0xFF), (byte)(length & 0xFF) };
            }
        }
    }
}
