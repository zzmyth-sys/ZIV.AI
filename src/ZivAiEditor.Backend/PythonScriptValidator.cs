using System;
using System.IO;

namespace ZivAiEditor.Backend;

/// <summary>
/// Validates the backend <c>main.py</c> path before a Python process is started (B). Surfaces a
/// readable error instead of the Win32 "directory name is invalid" from <c>Process.Start</c> when
/// the script is unset or its path does not exist.
/// </summary>
internal static class PythonScriptValidator
{
    public static void Validate(string script)
    {
        const string hint = "请在设置中配置 main.py 路径（[backend] script）。";
        if (string.IsNullOrWhiteSpace(script))
        {
            throw new ApplicationException($"Python 脚本路径未配置。{hint}");
        }

        var directory = Path.GetDirectoryName(script);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new ApplicationException($"Python 脚本目录不存在：{directory ?? script}。{hint}");
        }

        if (!File.Exists(script))
        {
            throw new ApplicationException($"Python 脚本不存在：{script}。{hint}");
        }
    }
}
