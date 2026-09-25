using System;

namespace SilentOrbit.ProtocolBuffers
{
    public enum Wire
    {
        Varint = 0,
        Fixed64 = 1,
        LengthDelimited = 2,
        [Obsolete]
        Start = 3,
        [Obsolete]
        End = 4,
        Fixed32 = 5
    }

    public class Key
    {
        public uint Field { get; set; }
        public Wire WireType { get; set; }

        public Key(uint field, Wire wireType)
        {
            this.Field = field;
            this.WireType = wireType;
        }

        public override string ToString()
        {
            return string.Format("[Key: {0}, {1}]", Field, WireType);
        }
    }

    public class KeyValue
    {
        public Key Key { get; set; }
        public byte[] Value { get; set; }

        public KeyValue(Key key, byte[] value)
        {
            this.Key = key;
            this.Value = value;
        }

        public override string ToString()
        {
            return string.Format("[KeyValue: {0}, {1}, {2} bytes]", Key.Field, Key.WireType, Value != null ? Value.Length : 0);
        }
    }

    public class ProtocolBufferException : Exception
    {
        public ProtocolBufferException(string message) : base(message) { }
    }
}
