/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using Heimdall.App.Views;
using Renci.SshNet.Common;

namespace Heimdall.App.Tests;

public sealed class ShellTransferFailureTests
{
    [Fact]
    public void ServerText_IsNotUsedAsMessage()
    {
        var failure = new SftpPermissionDeniedException("secret supplied by server");
        Assert.Equal("SftpErrorRemotePermissionDenied", ShellTransferFailure.MessageKey(failure));
        Assert.Equal("SftpStatusTransferFailed", ShellTransferFailure.MessageKey(new Exception("another secret")));
        var publicFailure = ShellTransferFailure.ToComException(failure);
        Assert.DoesNotContain("secret", publicFailure.Message);
        Assert.Equal(unchecked((int)0x80004005), publicFailure.HResult);
    }

    [Fact]
    public void LocalAccessFailure_UsesSpecificFixedMessage()
        => Assert.Equal("SftpErrorLocalAccessDenied", ShellTransferFailure.MessageKey(new UnauthorizedAccessException("secret")));
}
