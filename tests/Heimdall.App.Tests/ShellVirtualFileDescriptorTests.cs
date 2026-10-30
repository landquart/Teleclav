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

    [Fact]
    public void Create_MultipleFiles_PreservesOrderAnd64BitSizes()
    {
        SftpFileInfo[] files = [Entry("один.txt", RemoteEntryKind.File, 0), Entry("large.bin", RemoteEntryKind.File, 0x100000005L)];
        byte[] bytes = ShellVirtualFileDescriptor.Create(files);
        Assert.Equal(4 + (2 * 592), bytes.Length);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.StartsWith("один.txt\0", Encoding.Unicode.GetString(bytes, 76, 520));
        Assert.StartsWith("large.bin\0", Encoding.Unicode.GetString(bytes, 76 + 592, 520));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(68 + 592, 4)));
        Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(72 + 592, 4)));
    }

    [Fact]
    public void Create_CaseInsensitiveCollision_UsesDistinctName()
    {
        byte[] data = ShellVirtualFileDescriptor.Create(
            [Entry("a.txt", RemoteEntryKind.File, 1), Entry("A.TXT", RemoteEntryKind.File, 2)]);
        Assert.StartsWith("A (2).TXT\0", Encoding.Unicode.GetString(data, 76 + 592, 520));
    }

    [Fact]
    public void Create_DuplicateName_DoesNotTakeAnotherOriginalName()
    {
        byte[] data = ShellVirtualFileDescriptor.Create(
            [Entry("a.txt", RemoteEntryKind.File, 1), Entry("A.TXT", RemoteEntryKind.File, 2), Entry("a (2).txt", RemoteEntryKind.File, 3)]);
        Assert.StartsWith("A (3).TXT\0", Encoding.Unicode.GetString(data, 76 + 592, 520));
        Assert.StartsWith("a (2).txt\0", Encoding.Unicode.GetString(data, 76 + (2 * 592), 520));
    }

    [Fact]
    public void Create_HundredsOfFiles_ProducesMatchingDescriptorCount()
    {
        SftpFileInfo[] files = Enumerable.Range(0, 500)
            .Select(i => Entry($"file {i}.bin", RemoteEntryKind.File, i))
            .ToArray();
        byte[] bytes = ShellVirtualFileDescriptor.Create(files);
        Assert.Equal(4 + (files.Length * 592), bytes.Length);
        Assert.Equal(500u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.StartsWith("file 499.bin\0", Encoding.Unicode.GetString(bytes, 76 + (499 * 592), 520));
    }

    [Fact]
    public void Create_EmptySelection_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => ShellVirtualFileDescriptor.Create([]));
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
