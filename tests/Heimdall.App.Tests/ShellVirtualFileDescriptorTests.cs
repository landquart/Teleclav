/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Buffers.Binary;
using System.Text;
using Heimdall.App.Views;
using Heimdall.Sftp;

namespace Heimdall.App.Tests;

public sealed class ShellVirtualFileDescriptorTests
{
    [Fact]
    public void Create_OneFile_ContainsOneUnicodeDescriptorAndSize()
    {
        byte[] bytes = ShellVirtualFileDescriptor.Create(Entry("отчёт.txt", RemoteEntryKind.File, 123));
        Assert.Equal(596, bytes.Length);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.Equal(123u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(72, 4)));
        Assert.StartsWith("отчёт.txt\0", Encoding.Unicode.GetString(bytes, 76, 520));
    }

    [Theory]
    [InlineData("../bad")]
    [InlineData("CON.txt")]
    [InlineData("bad:name")]
    [InlineData("trailing.")]
    public void Create_UnsafeName_IsRefused(string name)
    {
        Assert.Throws<ArgumentException>(() => ShellVirtualFileDescriptor.Create(Entry(name, RemoteEntryKind.File, 1)));
    }

    [Fact]
    public void Create_Directory_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => ShellVirtualFileDescriptor.Create(Entry("folder", RemoteEntryKind.Directory, 0)));
    }

    private static SftpFileInfo Entry(string name, RemoteEntryKind kind, long size)
        => new(name, "/remote/" + name, kind, size, DateTime.UnixEpoch, "", "", "");
}
