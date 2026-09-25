using System;
using System.IO;
using System.Text;

namespace SilentOrbit.ProtocolBuffers
{
    public static class ProtocolParser
    {
        #region Keys

        public static Key ReadKey(Stream stream)
        {
            uint n = ReadUInt32(stream);
            return new Key(n >> 3, (Wire)(n & 0x07));
        }

        public static Key ReadKey(byte firstByte, Stream stream)
        {
            if (firstByte < 128)
                return new Key((uint)(firstByte >> 3), (Wire)(firstByte & 0x07));
            uint fieldID = ((uint)ReadUInt32(stream) << 4) | ((uint)(firstByte >> 3) & 0x0F);
            return new Key(fieldID, (Wire)(firstByte & 0x07));
        }

        public static void WriteKey(Stream stream, Key key)
        {
            uint n = (key.Field << 3) | ((uint)key.WireType);
            WriteUInt32(stream, n);
        }

        public static void SkipKey(Stream stream, Key key)
        {
            switch (key.WireType)
            {
                case Wire.Fixed32:
                    stream.Seek(4, SeekOrigin.Current);
                    return;
                case Wire.Fixed64:
                    stream.Seek(8, SeekOrigin.Current);
                    return;
                case Wire.LengthDelimited:
                    stream.Seek(ProtocolParser.ReadUInt32(stream), SeekOrigin.Current);
                    return;
                case Wire.Varint:
                    ProtocolParser.ReadSkipVarInt(stream);
                    return;
                default:
                    throw new ProtocolBufferException("Unknown wire type: " + key.WireType);
            }
        }

        public static byte[] ReadValueBytes(Stream stream, Key key)
        {
            byte[] b;
            int offset = 0;

            switch (key.WireType)
            {
                case Wire.Fixed32:
                    b = new byte[4];
                    while (offset < 4)
                        offset += stream.Read(b, offset, 4 - offset);
                    return b;
                case Wire.Fixed64:
                    b = new byte[8];
                    while (offset < 8)
                        offset += stream.Read(b, offset, 8 - offset);
                    return b;
                case Wire.LengthDelimited:
                    uint length = ProtocolParser.ReadUInt32(stream);
                    using (var ms = new MemoryStream())
                    {
                        ProtocolParser.WriteUInt32(ms, length);
                        b = new byte[length + ms.Length];
                        ms.ToArray().CopyTo(b, 0);
                        offset = (int)ms.Length;
                    }

                    while (offset < b.Length)
                        offset += stream.Read(b, offset, b.Length - offset);
                    return b;
                case Wire.Varint:
                    return ProtocolParser.ReadVarIntBytes(stream);
                default:
                    throw new ProtocolBufferException("Unknown wire type: " + key.WireType);
            }
        }

        #endregion

        #region Values

        public static string ReadString(Stream stream)
        {
            var bytes = ReadBytes(stream);
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }

        public static byte[] ReadBytes(Stream stream)
        {
            int length = (int)ReadUInt32(stream);
            byte[] buffer = new byte[length];
            int read = 0;
            while (read < length)
            {
                int r = stream.Read(buffer, read, length - read);
                if (r == 0)
                    throw new ProtocolBufferException("Expected " + (length - read) + " got " + read);
                read += r;
            }
            return buffer;
        }

        public static void SkipBytes(Stream stream)
        {
            int length = (int)ReadUInt32(stream);
            if (stream.CanSeek)
                stream.Seek(length, SeekOrigin.Current);
            else
                ReadBytes(stream);
        }

        public static void WriteString(Stream stream, string val)
        {
            WriteBytes(stream, Encoding.UTF8.GetBytes(val ?? ""));
        }

        public static void WriteBytes(Stream stream, byte[] val)
        {
            if (val == null) val = new byte[0];
            WriteUInt32(stream, (uint)val.Length);
            stream.Write(val, 0, val.Length);
        }

        #endregion

        #region VarInt, skip, read bytes

        public static void ReadSkipVarInt(Stream stream)
        {
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0)
                    throw new IOException("Stream ended too early");

                if ((b & 0x80) == 0)
                    return;
            }
        }

        public static byte[] ReadVarIntBytes(Stream stream)
        {
            byte[] buffer = new byte[10];
            int offset = 0;
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0)
                    throw new IOException("Stream ended too early");
                buffer[offset] = (byte)b;
                offset += 1;
                if ((b & 0x80) == 0)
                    break;
                if (offset >= buffer.Length)
                    throw new ProtocolBufferException("VarInt too long, more than 10 bytes");
            }
            byte[] ret = new byte[offset];
            Array.Copy(buffer, ret, ret.Length);
            return ret;
        }

        #endregion

        #region VarInt: int32, uint32, sint32

        public static int ReadZInt32(Stream stream)
        {
            uint val = ReadUInt32(stream);
            return (int)(val >> 1) ^ ((int)(val << 31) >> 31);
        }

        public static void WriteZInt32(Stream stream, int val)
        {
            WriteUInt32(stream, (uint)((val << 1) ^ (val >> 31)));
        }

        public static uint ReadUInt32(Stream stream)
        {
            uint val = 0;

            for (int n = 0; n < 5; n++)
            {
                int b = stream.ReadByte();
                if (b < 0)
                    throw new IOException("Stream ended too early");

                if ((n == 4) && (b & 0xF0) != 0)
                    throw new ProtocolBufferException("Got larger VarInt than 32bit unsigned");

                if ((b & 0x80) == 0)
                    return val | (uint)b << (7 * n);

                val |= (uint)(b & 0x7F) << (7 * n);
            }

            throw new ProtocolBufferException("Got larger VarInt than 32bit unsigned");
        }

        public static void WriteUInt32(Stream stream, uint val)
        {
            while (true)
            {
                byte b = (byte)(val & 0x7F);
                val = val >> 7;
                if (val == 0)
                {
                    stream.WriteByte(b);
                    break;
                }
                else
                {
                    b |= 0x80;
                    stream.WriteByte(b);
                }
            }
        }

        #endregion

        #region VarInt: int64, UInt64, SInt64

        public static long ReadZInt64(Stream stream)
        {
            ulong val = ReadUInt64(stream);
            return (long)(val >> 1) ^ ((long)(val << 63) >> 63);
        }

        public static void WriteZInt64(Stream stream, long val)
        {
            WriteUInt64(stream, (ulong)((val << 1) ^ (val >> 63)));
        }

        public static ulong ReadUInt64(Stream stream)
        {
            ulong val = 0;

            for (int n = 0; n < 10; n++)
            {
                int b = stream.ReadByte();
                if (b < 0)
                    throw new IOException("Stream ended too early");

                if ((n == 9) && (b & 0xFE) != 0)
                    throw new ProtocolBufferException("Got larger VarInt than 64 bit unsigned");

                if ((b & 0x80) == 0)
                    return val | (ulong)b << (7 * n);

                val |= (ulong)(b & 0x7F) << (7 * n);
            }

            throw new ProtocolBufferException("Got larger VarInt than 64 bit unsigned");
        }

        public static void WriteUInt64(Stream stream, ulong val)
        {
            while (true)
            {
                byte b = (byte)(val & 0x7F);
                val = val >> 7;
                if (val == 0)
                {
                    stream.WriteByte(b);
                    break;
                }
                else
                {
                    b |= 0x80;
                    stream.WriteByte(b);
                }
            }
        }

        #endregion

        #region Varint: bool

        public static bool ReadBool(Stream stream)
        {
            int b = stream.ReadByte();
            if (b < 0)
                throw new IOException("Stream ended too early");
            if (b == 1)
                return true;
            if (b == 0)
                return false;
            throw new ProtocolBufferException("Invalid boolean value");
        }

        public static void WriteBool(Stream stream, bool val)
        {
            stream.WriteByte(val ? (byte)1 : (byte)0);
        }

        #endregion
    }
}
