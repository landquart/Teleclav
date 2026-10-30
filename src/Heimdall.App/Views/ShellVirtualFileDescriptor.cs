/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Buffers.Binary;
using System.Text;
using Heimdall.Sftp;

namespace Heimdall.App.Views;

/// <summary>Creates one FILEGROUPDESCRIPTORW for an ordinary remote file.</summary>
internal static class ShellVirtualFileDescriptor
{
    internal static byte[] Create(SftpFileInfo entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string name = entry.Name;
        if (!CanCreate(entry))
        {
            throw new ArgumentException("The remote entry is not a file with a safe Windows name.", nameof(entry));
        }

        // FILEGROUPDESCRIPTORW = DWORD count followed by one 592-byte FILEDESCRIPTORW.
        byte[] data = new byte[596];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0x80000044); // Unicode, attributes, size
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(40), 0x80); // FILE_ATTRIBUTE_NORMAL
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(68), (uint)((ulong)entry.Size >> 32));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(72), (uint)entry.Size);
        Encoding.Unicode.GetBytes(name, data.AsSpan(76, 520));
        return data;
    }

    internal static bool CanCreate(SftpFileInfo entry)
        => entry.IsRegularFile && entry.Size >= 0 && IsSafeName(entry.Name);

    private static bool IsSafeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length >= 260 || name is "." or ".."
            || name.EndsWith(' ') || name.EndsWith('.')
            || name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)))
        {
            return false;
        }

        try
        {
            _ = new UnicodeEncoding(false, false, true).GetByteCount(name);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }

        string stem = name.Split('.')[0].TrimEnd(' ');
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
