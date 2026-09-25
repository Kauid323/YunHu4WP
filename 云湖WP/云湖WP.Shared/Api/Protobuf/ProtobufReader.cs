using System;
using System.IO;
using System.Text;

namespace 云湖WP.Api.Protobuf
{
    /// <summary>
    /// 轻量级、无第三方依赖的 Google Protocol Buffers 二进制流解码器 (兼容 WinRT / WP8.1)
    /// </summary>
    public static class ProtobufReader
    {
        /// <summary>
        /// 读取变长整数 Varint (uint64)
        /// </summary>
        public static ulong ReadVarint(Stream stream)
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                int b = stream.ReadByte();
                if (b == -1) throw new EndOfStreamException("Protobuf Varint unexpected end of stream.");
                result |= ((ulong)(b & 0x7F)) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
                if (shift >= 64) break;
            }
            return result;
        }

        /// <summary>
        /// 读取带长度前缀的字节数组 (WireType = 2)
        /// </summary>
        public static byte[] ReadBytes(Stream stream)
        {
            int length = (int)ReadVarint(stream);
            if (length <= 0) return new byte[0];

            byte[] buffer = new byte[length];
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = stream.Read(buffer, totalRead, length - totalRead);
                if (read <= 0) break;
                totalRead += read;
            }
            return buffer;
        }

        /// <summary>
        /// 读取 UTF-8 字符串 (WireType = 2)
        /// </summary>
        public static string ReadString(Stream stream)
        {
            byte[] bytes = ReadBytes(stream);
            if (bytes == null || bytes.Length == 0) return "";
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// 跳过未知或不关心的字段
        /// </summary>
        public static void SkipField(Stream stream, int wireType)
        {
            switch (wireType)
            {
                case 0: // Varint
                    ReadVarint(stream);
                    break;
                case 1: // 64-bit fixed
                    stream.Seek(8, SeekOrigin.Current);
                    break;
                case 2: // Length-delimited
                    int len = (int)ReadVarint(stream);
                    if (len > 0)
                    {
                        stream.Seek(len, SeekOrigin.Current);
                    }
                    break;
                case 5: // 32-bit fixed
                    stream.Seek(4, SeekOrigin.Current);
                    break;
                default:
                    throw new InvalidOperationException(string.Format("Unsupported wire type {0}", wireType));
            }
        }
    }
}
