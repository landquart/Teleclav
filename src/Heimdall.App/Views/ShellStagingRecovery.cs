/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Runtime.Versioning;
using Heimdall.Core.Logging;
using Result = Heimdall.App.Views.ShellStagingGuard.RecoveryResult;

namespace Heimdall.App.Views;

[SupportedOSPlatform("windows")]
internal static class ShellStagingRecovery
{
    internal static Result Recover(string root, CancellationToken cancellationToken = default, int maximumEntries = 128)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        if (maximumEntries > 4096) { throw new ArgumentOutOfRangeException(nameof(maximumEntries)); }
        try { return ShellStagingGuard.RecoverAbandoned(root, maximumEntries, cancellationToken); }
        catch (IOException ex) when (ShellStagingGuard.IsMissingRecoveryRoot(ex)) { return default; }
    }

    internal static Task RunStartupAsync(CancellationToken cancellationToken)
        => RunAsync(ShellStagingGuard.DefaultRootPath, cancellationToken, TimeSpan.FromSeconds(2));

    internal static async Task RunAsync(string root, CancellationToken cancellationToken, TimeSpan retryDelay,
        Action<Result>? report = null)
    {
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt != 0) { await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false); }
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                budget.CancelAfter(TimeSpan.FromSeconds(5));
                Result result;
                try { result = await Task.Run(() => Recover(root, budget.Token), cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Cancellation is cooperative between filesystem calls; a stalled driver cannot be forcibly timed out.
                    FileLogger.Warn("Shell staging recovery reached its time budget; temporary data may remain.");
                    continue;
                }
                report?.Invoke(result);
                if (result.Retained == 0 && !result.Limited) { return; }
                FileLogger.Warn($"Shell staging recovery: inspected={result.Inspected}, removed={result.Removed}, retained={result.Retained}, limited={result.Limited}.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            FileLogger.Warn("Shell staging recovery refused the storage root; temporary data may remain (" + ex.GetType().Name + ").");
        }
    }
}
