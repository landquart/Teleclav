/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using Heimdall.App.Views;

if (!OperatingSystem.IsWindows()) { return 2; }
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
using var operation = ShellStagingGuard.CreateAt(args[0]);
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
