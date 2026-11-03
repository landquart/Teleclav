/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using Heimdall.App.Views;

if (!OperatingSystem.IsWindows()) { return 2; }
if (args[1] == "scan")
{
    ShellStagingGuard.RecoverAbandoned(args[0], 128, CancellationToken.None);
    return 0;
}
if (args[1] == "recover")
{
    try { using var recovered = ShellStagingGuard.Reopen(args[0]); recovered.Delete(); return 0; }
    catch (IOException) { return 11; }
}
if (args[1] == "lock")
{
    using var file = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.Read);
    Console.WriteLine("ready");
    Console.Out.Flush();
    Console.ReadLine();
    return 0;
}
if (args[1].StartsWith("kill:", StringComparison.Ordinal))
{
    string boundary = args[1][5..];
    if (boundary == "recovery")
    {
        using var recovered = ShellStagingGuard.Reopen(args[0]);
        recovered.Fault = current => { if (current == ShellStagingPoint.AfterFileDelete) { PauseForTermination(); } };
        recovered.Delete();
        return 3;
    }
    ShellStagingPoint? stop = boundary switch
    {
        "directory" => ShellStagingPoint.AfterOperationDirectory,
        "journal" => ShellStagingPoint.AfterOwnershipJournal,
        "reader-open" or "reader-closed" => null,
        _ => Enum.Parse<ShellStagingPoint>(boundary)
    };
    using var owned = ShellStagingGuard.CreateAt(args[0], current =>
    {
        if (current == stop) { PauseForTermination(); }
    });
    await owned.DownloadAsync("0", 5, async (stream, ct) =>
    {
        // DuringWrite now stops with two of the five bytes written, not a completed file.
        await stream.WriteAsync("he"u8.ToArray(), ct);
        await stream.WriteAsync("llo"u8.ToArray(), ct);
    }, CancellationToken.None);
    if (boundary is "reader-open" or "reader-closed")
    {
        using (var reader = new FileStream(Path.Combine(owned.DirectoryPath, "0"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (reader.ReadByte() != 'h') { return 4; }
            if (boundary == "reader-open") { PauseForTermination(); }
        }
        PauseForTermination();
    }
    owned.Delete();
    return 3;
}
using var operation = ShellStagingGuard.CreateAt(args[0]);
if (args[1] == "transfer")
{
    await operation.DownloadAsync("0", 5, (stream, ct) => stream.WriteAsync("hello"u8.ToArray(), ct).AsTask(), CancellationToken.None);
    if (File.ReadAllText(Path.Combine(operation.DirectoryPath, "0")) != "hello") { return 4; }
    operation.Delete();
    return 0;
}
if (args[1] is "hold" or "partial")
{
    await operation.DownloadAsync("0", 5, (stream, ct) => stream.WriteAsync("hello"u8.ToArray(), ct).AsTask(), CancellationToken.None);
    if (args[1] == "hold")
    {
        Console.WriteLine(operation.DirectoryPath);
        Console.Out.Flush();
        Console.ReadLine();
        return 0; // Release the lease, retain the owned object for the next process.
    }
    await operation.DownloadAsync("1", 5, (stream, ct) => stream.WriteAsync("hello"u8.ToArray(), ct).AsTask(), CancellationToken.None);
    operation.Fault = point => { if (point == ShellStagingPoint.AfterFileDelete) { Environment.Exit(37); } };
    operation.Delete();
    return 3;
}
int point = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
operation.Fault = current => { if ((int)current == point) { Environment.Exit(37); } };
await operation.DownloadAsync("0", 5,
    (stream, ct) => stream.WriteAsync("hello"u8.ToArray(), ct).AsTask(), CancellationToken.None);
operation.Delete();
return 3; // Every requested boundary must actually have been reached.

static void PauseForTermination()
{
    Console.WriteLine("ready");
    Console.Out.Flush();
    Console.ReadLine(); // Parent retains stdin while the journal/reader/native handles remain live.
    Environment.Exit(3); // An orderly release is a test failure, never crash evidence.
}
