using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PsdTool.Parser
{
    /// <summary>文字图层 EngineData（类 PostScript 格式）解析。</summary>
    internal sealed class EngineDataParser
    {
        readonly byte[] _d;
        int _p;

        EngineDataParser(byte[] data)
        {
            _d = data;
        }

        public static Dictionary<string, object> Parse(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            return new EngineDataParser(data).ReadValue() as Dictionary<string, object>;
        }

        static bool IsWhitespace(byte b) => b == ' ' || b == '\t' || b == '\r' || b == '\n' || b == 0;

        static bool IsDelimiter(byte b) =>
            IsWhitespace(b) || b == '/' || b == '[' || b == ']' || b == '(' || b == ')' || b == '<' || b == '>';

        void SkipWhitespace()
        {
            while (_p < _d.Length && IsWhitespace(_d[_p])) _p++;
        }

        bool At(char a, char b) => _p + 1 < _d.Length && _d[_p] == a && _d[_p + 1] == b;

        object ReadValue()
        {
            SkipWhitespace();
            if (_p >= _d.Length) return null;
            if (At('<', '<'))
            {
                _p += 2;
                return ReadDict();
            }

            byte c = _d[_p];
            if (c == '[')
            {
                _p++;
                return ReadList();
            }
            if (c == '(')
            {
                _p++;
                return ReadString();
            }
            if (c == '/')
            {
                _p++;
                return ReadToken();
            }

            string token = ReadToken();
            if (token.Length == 0)
            {
                _p++;
                return null;
            }
            if (token == "true") return true;
            if (token == "false") return false;
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return v;
            return token;
        }

        Dictionary<string, object> ReadDict()
        {
            var dict = new Dictionary<string, object>();
            while (true)
            {
                SkipWhitespace();
                if (_p >= _d.Length) break;
                if (At('>', '>'))
                {
                    _p += 2;
                    break;
                }
                if (_d[_p] != '/')
                {
                    _p++;
                    continue;
                }
                _p++;
                string key = ReadToken();
                dict[key] = ReadValue();
            }
            return dict;
        }

        List<object> ReadList()
        {
            var list = new List<object>();
            while (true)
            {
                SkipWhitespace();
                if (_p >= _d.Length) break;
                if (_d[_p] == ']')
                {
                    _p++;
                    break;
                }
                list.Add(ReadValue());
            }
            return list;
        }

        string ReadToken()
        {
            int start = _p;
            while (_p < _d.Length && !IsDelimiter(_d[_p])) _p++;
            return Encoding.ASCII.GetString(_d, start, _p - start);
        }

        string ReadString()
        {
            var buf = new List<byte>();
            while (_p < _d.Length)
            {
                byte b = _d[_p++];
                if (b == '\\' && _p < _d.Length)
                {
                    buf.Add(_d[_p++]);
                    continue;
                }
                if (b == ')') break;
                buf.Add(b);
            }

            var bytes = buf.ToArray();
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            return Encoding.UTF8.GetString(bytes);
        }
    }

    internal static class EngineDataPath
    {
        /// <summary>按路径取值，字符串表示字典键，整数表示列表下标。</summary>
        public static object Get(object node, params object[] path)
        {
            foreach (var key in path)
            {
                if (node == null) return null;
                if (key is string s)
                {
                    if (node is Dictionary<string, object> d && d.TryGetValue(s, out var v)) node = v;
                    else return null;
                }
                else if (key is int i)
                {
                    if (node is List<object> l && i >= 0 && i < l.Count) node = l[i];
                    else return null;
                }
            }
            return node;
        }

        public static double Num(object v, double fallback)
        {
            if (v is double d) return d;
            if (v is bool b) return b ? 1 : 0;
            return fallback;
        }
    }
}
