/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
namespace Heimdall.Core.Logging;

// The fixture source-links the product scheduler. Its fixed/count-only warnings go to stderr
// instead of creating an application profile or introducing application service dependencies.
internal static class FileLogger
{
    internal static void Warn(string message) => Console.Error.WriteLine(message);
}
