using System.Collections.Generic;
using PsdTool.Parser;
using UnityEngine;

namespace PsdTool
{
    /// <summary>从 lfx2 描述符提取文字描边与投影；其它效果标记为不支持。</summary>
    public static class EffectsReader
    {
        public static void ApplyToText(PsdLayer layer, TextInfo text, List<string> warnings)
        {
            if (layer?.Effects == null || text == null) return;
            var fx = layer.Effects;
            if (!fx.Get("masterFXSwitch", true)) return;

            text.outline = ReadStroke(fx) ?? text.outline;
            text.shadow = ReadShadow(fx) ?? text.shadow;

            if (HasEnabled(fx, "IrGl") || HasEnabled(fx, "OrGl") || HasEnabled(fx, "ebbl")
                || HasEnabled(fx, "GrFl") || HasEnabled(fx, "gradientFill"))
            {
                text.hasUnsupportedEffects = true;
                warnings?.Add($"文字“{layer.Name}”含内发光/外发光/斜面/渐变等 TMP 不支持的效果，已跳过（描边与投影若有仍会同步）");
            }
        }

        static TextEffectInfo ReadStroke(PsdDescriptor fx)
        {
            var d = GetEffect(fx, "FrFX");
            if (d == null || !d.Get("enab", false)) return null;
            return new TextEffectInfo
            {
                enabled = true,
                color = ReadColor(d, "Clr "),
                size = (float)Unit(d, "Sz  ", 3),
                opacity = (float)Unit(d, "Opct", 100) / 100f,
            };
        }

        static TextEffectInfo ReadShadow(PsdDescriptor fx)
        {
            var d = GetEffect(fx, "DrSh");
            if (d == null || !d.Get("enab", false)) return null;
            return new TextEffectInfo
            {
                enabled = true,
                color = ReadColor(d, "Clr "),
                size = (float)Unit(d, "blur", 5),
                distance = (float)Unit(d, "Dstn", 5),
                angle = (float)Unit(d, "lagl", 120),
                opacity = (float)Unit(d, "Opct", 75) / 100f,
            };
        }

        static PsdDescriptor GetEffect(PsdDescriptor fx, string key)
        {
            if (fx.Items.TryGetValue(key, out var v) && v is PsdDescriptor d) return d;
            return null;
        }

        static bool HasEnabled(PsdDescriptor fx, string key)
        {
            var d = GetEffect(fx, key);
            return d != null && d.Get("enab", false);
        }

        static double Unit(PsdDescriptor d, string key, double fallback)
        {
            if (!d.Items.TryGetValue(key, out var v)) return fallback;
            if (v is PsdUnitFloat u) return u.Value;
            if (v is double dbl) return dbl;
            if (v is int i) return i;
            return fallback;
        }

        static Color32 ReadColor(PsdDescriptor d, string key)
        {
            if (!(d.Items.TryGetValue(key, out var v) && v is PsdDescriptor c))
                return new Color32(0, 0, 0, 255);
            // RGB as 0..255 doubles in Photoshop descriptors
            byte r = ToByte(c.Get("Rd  ", 0.0));
            byte g = ToByte(c.Get("Grn ", 0.0));
            byte b = ToByte(c.Get("Bl  ", 0.0));
            return new Color32(r, g, b, 255);
        }

        static byte ToByte(double v) => (byte)Mathf.Clamp(Mathf.RoundToInt((float)v), 0, 255);
    }
}
