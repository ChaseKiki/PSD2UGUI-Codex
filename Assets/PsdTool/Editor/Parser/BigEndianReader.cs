using System;
using System.IO;
using System.Text;

namespace PsdTool.Parser
{
    internal sealed class BigEndianReader
    {
        readonly Stream _stream;
        readonly byte[] _buffer = new byte[8];

        public BigEndianReader(Stream stream)
        {
            _stream = stream;
        }

        public long Position
        {
            get => _stream.Position;
            set => _stream.Position = value;
        }

        public long Length => _stream.Length;

        void Fill(byte[] target, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = _stream.Read(target, read, count - read);
                if (n <= 0) throw new EndOfStreamException("PSD 文件意外结束");
                read += n;
            }
        }

        public byte ReadByte()
        {
            Fill(_buffer, 1);
            return _buffer[0];
        }

        public ushort ReadUInt16()
        {
            Fill(_buffer, 2);
            return (ushort)((_buffer[0] << 8) | _buffer[1]);
        }

        public short ReadInt16() => (short)ReadUInt16();

        public uint ReadUInt32()
        {
            Fill(_buffer, 4);
            return ((uint)_buffer[0] << 24) | ((uint)_buffer[1] << 16) | ((uint)_buffer[2] << 8) | _buffer[3];
        }

        public int ReadInt32() => (int)ReadUInt32();

        public ulong ReadUInt64()
        {
            ulong hi = ReadUInt32();
            ulong lo = ReadUInt32();
            return (hi << 32) | lo;
        }

        public long ReadInt64() => (long)ReadUInt64();

        public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());

        public byte[] ReadBytes(int count)
        {
            var bytes = new byte[count];
            if (count > 0) Fill(bytes, count);
            return bytes;
        }

        public string ReadAscii(int count) => Encoding.ASCII.GetString(ReadBytes(count));

        public void Skip(long count) => _stream.Position += count;

        /// <summary>PSB 中部分长度字段为 8 字节。</summary>
        public long ReadLength(bool isPsb) => isPsb ? ReadInt64() : ReadUInt32();

        public string ReadPascalString(int padding)
        {
            int len = ReadByte();
            string s = Encoding.ASCII.GetString(ReadBytes(len));
            int total = len + 1;
            Skip((padding - total % padding) % padding);
            return s;
        }

        public string ReadUnicodeString()
        {
            uint count = ReadUInt32();
            if (count == 0) return string.Empty;
            return Encoding.BigEndianUnicode.GetString(ReadBytes((int)count * 2)).TrimEnd('\0');
        }
    }
}
