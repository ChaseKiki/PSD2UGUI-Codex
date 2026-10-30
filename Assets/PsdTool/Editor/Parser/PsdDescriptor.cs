using System.Collections.Generic;

namespace PsdTool.Parser
{
    public sealed class PsdDescriptor
    {
        public string Name;
        public string ClassId;
        public readonly Dictionary<string, object> Items = new Dictionary<string, object>();

        public T Get<T>(string key, T fallback)
        {
            return Items.TryGetValue(key, out var v) && v is T t ? t : fallback;
        }
    }

    public readonly struct PsdUnitFloat
    {
        public readonly string Unit;
        public readonly double Value;

        public PsdUnitFloat(string unit, double value)
        {
            Unit = unit;
            Value = value;
        }
    }

    public readonly struct PsdEnum
    {
        public readonly string Type;
        public readonly string Value;

        public PsdEnum(string type, string value)
        {
            Type = type;
            Value = value;
        }
    }

    /// <summary>Photoshop Action Descriptor 格式。</summary>
    internal static class DescriptorReader
    {
        public static PsdDescriptor ReadDescriptor(BigEndianReader r)
        {
            var d = new PsdDescriptor
            {
                Name = r.ReadUnicodeString(),
                ClassId = ReadKey(r),
            };
            uint count = r.ReadUInt32();
            for (uint i = 0; i < count; i++)
            {
                string key = ReadKey(r);
                string type = r.ReadAscii(4);
                d.Items[key] = ReadValue(r, type);
            }
            return d;
        }

        static string ReadKey(BigEndianReader r)
        {
            uint len = r.ReadUInt32();
            return r.ReadAscii(len == 0 ? 4 : (int)len);
        }

        static object ReadValue(BigEndianReader r, string type)
        {
            switch (type)
            {
                case "obj ":
                    ReadReference(r);
                    return null;
                case "Objc":
                case "GlbO":
                    return ReadDescriptor(r);
                case "VlLs":
                {
                    uint n = r.ReadUInt32();
                    var list = new List<object>((int)n);
                    for (uint i = 0; i < n; i++)
                        list.Add(ReadValue(r, r.ReadAscii(4)));
                    return list;
                }
                case "doub":
                    return r.ReadDouble();
                case "UntF":
                {
                    string unit = r.ReadAscii(4);
                    return new PsdUnitFloat(unit, r.ReadDouble());
                }
                case "UnFl":
                {
                    r.ReadAscii(4);
                    uint n = r.ReadUInt32();
                    var arr = new double[n];
                    for (uint i = 0; i < n; i++) arr[i] = r.ReadDouble();
                    return arr;
                }
                case "TEXT":
                    return r.ReadUnicodeString();
                case "enum":
                {
                    string t = ReadKey(r);
                    return new PsdEnum(t, ReadKey(r));
                }
                case "long":
                    return r.ReadInt32();
                case "comp":
                    return r.ReadInt64();
                case "bool":
                    return r.ReadByte() != 0;
                case "type":
                case "GlbC":
                    r.ReadUnicodeString();
                    return ReadKey(r);
                case "alis":
                case "Pth ":
                case "tdta":
                    return r.ReadBytes((int)r.ReadUInt32());
                case "ObAr":
                    r.ReadUInt32();
                    return ReadDescriptor(r);
                default:
                    throw new PsdException($"未知的描述符类型 '{type}'");
            }
        }

        static void ReadReference(BigEndianReader r)
        {
            uint count = r.ReadUInt32();
            for (uint i = 0; i < count; i++)
            {
                string type = r.ReadAscii(4);
                switch (type)
                {
                    case "prop":
                        r.ReadUnicodeString(); ReadKey(r); ReadKey(r);
                        break;
                    case "Clss":
                        r.ReadUnicodeString(); ReadKey(r);
                        break;
                    case "Enmr":
                        r.ReadUnicodeString(); ReadKey(r); ReadKey(r); ReadKey(r);
                        break;
                    case "rele":
                        r.ReadUnicodeString(); ReadKey(r); r.ReadUInt32();
                        break;
                    case "Idnt":
                    case "indx":
                        r.ReadUInt32();
                        break;
                    case "name":
                        r.ReadUnicodeString(); ReadKey(r); r.ReadUnicodeString();
                        break;
                    default:
                        throw new PsdException($"未知的引用类型 '{type}'");
                }
            }
        }
    }
}
