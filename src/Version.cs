using System.Reflection;

// 程序版本：与 GitHub Release 标签保持一致
[assembly: AssemblyTitle("VpnTray")]
[assembly: AssemblyDescription("Lightweight tray GUI for SHIEP-Pipeline (SOCKS5 core)")]
[assembly: AssemblyProduct("VpnTray")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1.0")]

namespace VpnTray
{
    /// <summary>给界面与自检用的版本字符串（AssemblyInformationalVersion 带版权等后缀，这里单独写死更省事）。</summary>
    internal static class AppInfo
    {
        public const string Version = "1.1.0";
    }
}
