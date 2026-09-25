using System;
using System.IO;
using System.Text;

namespace 云湖WP.Api.Protobuf
{
    /// <summary>
    /// 轻量级 Protocol Buffers 二进制流编码器
    /// </summary>
    public static class ProtobufWriter
    {
        /// <summary>
        /// 写入变长整数 Varint
        /// </summary>
        public static void WriteVarint(Stream stream, ulong value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            stream.WriteByte((byte)value);
        }

        /// <summary>
        /// 写入字段 Tag
        /// </summary>
        public static void WriteTag(Stream stream, int fieldNumber, int wireType)
        {
            ulong tag = (ulong)((fieldNumber << 3) | (wireType & 0x07));
            WriteVarint(stream, tag);
        }

        /// <summary>
        /// 写入 Varint 整数类型字段 (int32, int64, uint32, uint64, bool, enum)
        /// </summary>
        public static void WriteInt64(Stream stream, int fieldNumber, long value)
        {
            WriteTag(stream, fieldNumber, 0);
            WriteVarint(stream, (ulong)value);
        }

        /// <summary>
        /// 写入 UTF-8 字符串字段 (WireType = 2)
        /// </summary>
        public static void WriteString(Stream stream, int fieldNumber, string value)
        {
            if (value == null) return;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteTag(stream, fieldNumber, 2);
            WriteVarint(stream, (ulong)bytes.Length);
            if (bytes.Length > 0)
            {
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        /// <summary>
        /// 写入字节数组字段 (WireType = 2)
        /// </summary>
        public static void WriteBytes(Stream stream, int fieldNumber, byte[] bytes)
        {
            if (bytes == null) return;
            WriteTag(stream, fieldNumber, 2);
            WriteVarint(stream, (ulong)bytes.Length);
            if (bytes.Length > 0)
            {
                stream.Write(bytes, 0, bytes.Length);
            }
        }
    }
}
