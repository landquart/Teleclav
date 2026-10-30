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
        return Create([entry]);
    }

    internal static byte[] Create(IReadOnlyList<SftpFileInfo> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            throw new ArgumentException("At least one file is required.", nameof(entries));
        }

        HashSet<string> reserved = new(StringComparer.OrdinalIgnoreCase);
        foreach (SftpFileInfo entry in entries)
        {
            if (entry is null || !CanCreate(entry))
            {
                throw new ArgumentException("The selection contains an invalid file name or type.", nameof(entries));
            }

            reserved.Add(entry.Name);
        }

        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        string[] names = new string[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            string name = entries[i].Name;
            if (!used.Add(name))
            {
                int dot = name.LastIndexOf('.');
                string stem = dot > 0 ? name[..dot] : name;
                string extension = dot > 0 ? name[dot..] : string.Empty;
                int suffix = 2;
                string candidate;
                do
                {
                    candidate = $"{stem} ({suffix++}){extension}";
                    if (!WindowsFileName.IsSafeName(candidate))
                    {
                        throw new ArgumentException("A duplicate name cannot be safely disambiguated.", nameof(entries));
                    }
                }
                while (reserved.Contains(candidate) || !used.Add(candidate));

                name = candidate;
            }

            names[i] = name;
        }

        // FILEGROUPDESCRIPTORW is a DWORD count followed by 592-byte descriptors.
        byte[] data = new byte[checked(4 + entries.Count * 592)];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            SftpFileInfo entry = entries[i];
            Span<byte> descriptor = data.AsSpan(4 + (i * 592), 592);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor, 0x80000044);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[36..], 0x80);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[64..], (uint)((ulong)entry.Size >> 32));
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[68..], (uint)entry.Size);
            Encoding.Unicode.GetBytes(names[i], descriptor.Slice(72, 520));
        }

        return data;
    }

    /// <summary>Creates Shell descriptors for an already validated directory hierarchy.</summary>
    internal static byte[] CreateHierarchy(IReadOnlyList<ShellFolderItem> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0) { throw new ArgumentException("The selection is empty.", nameof(entries)); }
        byte[] data = new byte[checked(4 + entries.Count * 592)];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            string name = entry.RelativePath.Replace('/', '\\');
            if (name.Length >= 260 || entry.Size < 0 || name.Split('\\').Any(part => !WindowsFileName.IsSafeName(part)))
                throw new ArgumentException("Invalid Shell hierarchy path.", nameof(entries));
            Span<byte> descriptor = data.AsSpan(4 + i * 592, 592);
            bool isDirectory = entry.Kind == RemoteDownloadOpKind.MakeDirectory;
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor, isDirectory ? 0x80000004u : 0x80000044u);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[36..], isDirectory ? 0x10u : 0x80u);
            if (!isDirectory)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(descriptor[64..], (uint)((ulong)entry.Size >> 32));
                BinaryPrimitives.WriteUInt32LittleEndian(descriptor[68..], (uint)entry.Size);
            }
            Encoding.Unicode.GetBytes(name, descriptor.Slice(72, 520));
        }
        return data;
    }

    internal static bool CanCreate(SftpFileInfo entry)
        => entry.IsRegularFile && entry.Size >= 0 && WindowsFileName.IsSafeName(entry.Name);

}
