/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Text;

namespace Heimdall.Sftp;

/// <summary>Checks whether a single file name can be represented safely on Windows.</summary>
public static class WindowsFileName
{
    private static readonly UnicodeEncoding StrictUnicode = new(false, false, true);

    public static bool IsSafeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name is "." or ".."
            || name.EndsWith(' ') || name.EndsWith('.')
            || name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)))
        {
            return false;
        }

        try
        {
            _ = StrictUnicode.GetByteCount(name);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }

        int dot = name.IndexOf('.');
        ReadOnlySpan<char> stem = name.AsSpan(0, dot < 0 ? name.Length : dot).TrimEnd(' ');
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
            || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && (stem[3] is >= '1' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3'));
    }
}
