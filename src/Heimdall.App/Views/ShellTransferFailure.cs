/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Runtime.InteropServices;
using Renci.SshNet.Common;

namespace Heimdall.App.Views;

/// <summary>Maps Shell transfer errors to fixed messages without displaying server-supplied text.</summary>
internal static class ShellTransferFailure
{
    internal static COMException ToComException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        int code = exception is OperationCanceledException ? unchecked((int)0x800704C7) : unchecked((int)0x80004005);
        return new COMException("The file transfer did not complete.", code);
    }

    internal static string MessageKey(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is UnauthorizedAccessException) { return "SftpErrorLocalAccessDenied"; }
        if (exception is SftpPermissionDeniedException) { return "SftpErrorRemotePermissionDenied"; }
        if (exception is IOException && (exception.HResult & 0xffff) is 0x27 or 0x70)
            return "SftpErrorLocalDiskFull";
        return "SftpStatusTransferFailed";
    }
}
