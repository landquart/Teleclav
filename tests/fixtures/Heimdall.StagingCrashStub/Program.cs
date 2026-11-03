/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using Heimdall.App.Views;

if (!OperatingSystem.IsWindows()) { return 2; }
using var operation = ShellStagingGuard.CreateAt(args[0]);
int point = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
operation.Fault = current => { if ((int)current == point) { Environment.Exit(37); } };
await operation.DownloadAsync("0", 5,
    (stream, ct) => stream.WriteAsync("hello"u8.ToArray(), ct).AsTask(), CancellationToken.None);
operation.Delete();
return 3; // Every requested boundary must actually have been reached.
