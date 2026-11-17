using System;
using System.Drawing;
using Microsoft.Win32;

namespace VpnTray
{
    /// <summary>浅色 / 深色 / 跟随系统 三套配色。</summary>
    internal sealed class Theme
    {
        public bool Dark;
        public Color Bg, Card, Border, Text, Sub, Accent, AccentHover, AccentPress, OnAccent,
                     Ctrl, CtrlBorder, CtrlText, LogBg, LogText, Divider,
                     Ok, Warn, Err, Idle;

        public static Theme Current;

        private static Theme BuildLight()
        {
            Theme t = new Theme();
            t.Dark = false;
            t.Bg = Color.FromArgb(0xF3, 0xF4, 0xF6);
            t.Card = Color.FromArgb(0xFF, 0xFF, 0xFF);
            t.Border = Color.FromArgb(0xE1, 0xE5, 0xEA);
            t.Text = Color.FromArgb(0x16, 0x18, 0x1D);
            t.Sub = Color.FromArgb(0x6B, 0x72, 0x80);
            t.Accent = Color.FromArgb(0x2F, 0x6F, 0xED);
            t.AccentHover = Color.FromArgb(0x25, 0x5F, 0xD1);
            t.AccentPress = Color.FromArgb(0x1E, 0x4F, 0xB0);
            t.OnAccent = Color.White;
            t.Ctrl = Color.FromArgb(0xF7, 0xF8, 0xFA);
            t.CtrlBorder = Color.FromArgb(0xD6, 0xDB, 0xE2);
            t.CtrlText = Color.FromArgb(0x16, 0x18, 0x1D);
            t.LogBg = Color.FromArgb(0xFB, 0xFC, 0xFD);
            t.LogText = Color.FromArgb(0x24, 0x29, 0x2F);
            t.Divider = Color.FromArgb(0xEC, 0xEF, 0xF2);
            t.Ok = Color.FromArgb(0x16, 0xA3, 0x4A);
            t.Warn = Color.FromArgb(0xD9, 0x77, 0x06);
            t.Err = Color.FromArgb(0xDC, 0x26, 0x26);
            t.Idle = Color.FromArgb(0x9C, 0xA3, 0xAF);
            return t;
        }

        private static Theme BuildDark()
        {
            Theme t = new Theme();
            t.Dark = true;
            t.Bg = Color.FromArgb(0x14, 0x17, 0x1C);
            t.Card = Color.FromArgb(0x1B, 0x1F, 0x26);
            t.Border = Color.FromArgb(0x2A, 0x30, 0x38);
            t.Text = Color.FromArgb(0xE6, 0xE9, 0xEE);
            t.Sub = Color.FromArgb(0x98, 0xA2, 0xB3);
            t.Accent = Color.FromArgb(0x4C, 0x8D, 0xFF);
            t.AccentHover = Color.FromArgb(0x5D, 0x99, 0xFF);
            t.AccentPress = Color.FromArgb(0x3D, 0x7B, 0xE8);
            t.OnAccent = Color.White;
            t.Ctrl = Color.FromArgb(0x23, 0x28, 0x30);
            t.CtrlBorder = Color.FromArgb(0x33, 0x3B, 0x45);
            t.CtrlText = Color.FromArgb(0xE6, 0xE9, 0xEE);
            t.LogBg = Color.FromArgb(0x0F, 0x12, 0x16);
            t.LogText = Color.FromArgb(0xC8, 0xD1, 0xDB);
            t.Divider = Color.FromArgb(0x25, 0x2A, 0x32);
            t.Ok = Color.FromArgb(0x3F, 0xD6, 0x8A);
            t.Warn = Color.FromArgb(0xF5, 0xB4, 0x4C);
            t.Err = Color.FromArgb(0xF8, 0x71, 0x71);
            t.Idle = Color.FromArgb(0x5C, 0x66, 0x75);
            return t;
        }

        /// <summary>name: light | dark | system</summary>
        public static Theme Resolve(string name)
        {
            if (string.Equals(name, "dark", StringComparison.OrdinalIgnoreCase)) return BuildDark();
            if (string.Equals(name, "light", StringComparison.OrdinalIgnoreCase)) return BuildLight();
            return SystemPrefersDark() ? BuildDark() : BuildLight();
        }

        public static bool SystemPrefersDark()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("AppsUseLightTheme");
                        if (v is int) return ((int)v) == 0;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>主题无关的语义色（状态点/日志级别）。</summary>
        public Color StateColor(ConnState s)
        {
            switch (s)
            {
                case ConnState.Connecting: return Warn;
                case ConnState.Connected: return Ok;
                case ConnState.Error: return Err;
                default: return Idle;
            }
        }
    }
}
