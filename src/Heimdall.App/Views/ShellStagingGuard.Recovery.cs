/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.ComponentModel;
using System.IO;

namespace Heimdall.App.Views;

internal sealed partial class ShellStagingGuard
{
    internal readonly record struct RecoveryResult(int Inspected, int Removed, int Retained, bool Limited);

    // A discovery owner pins only existing ancestors/root. It never creates an operation or repairs ACLs.
    private ShellStagingGuard(string root)
    {
        ValidateInteropLayout();
        ValidatePath(root);
        _path = Path.GetFullPath(root);
        try
        {
            OpenTrustedRoot(_path, create: false);
            _rootIdentity = Identity(_ancestors[^1]);
        }
        catch { Dispose(); throw; }
    }

    internal static RecoveryResult RecoverAbandoned(string root, int maximumEntries, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var discovery = new ShellStagingGuard(root);
        int inspected = 0, removed = 0, retained = 0;
        // Snapshot a bounded set before our deletions change the enumeration cursor's directory.
        string[] names = EnumerateNames(discovery._ancestors[^1], cancellationToken).Take(maximumEntries + 1).ToArray();
        foreach (string name in names.Take(maximumEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            inspected++;
            if (!Guid.TryParseExact(name, "N", out _)) { retained++; continue; }
            try
            {
                // The discovery owner still pins every ancestor without delete sharing. Reopen resolves
                // the same chain relatively and validates the exclusive lease, header and operation ID.
                using var operation = Reopen(Path.Combine(discovery._path, name));
                if (operation._rootIdentity != discovery._rootIdentity)
                    throw new IOException("Recovery staging root identity changed.");
                operation.Delete(cancellationToken);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Locked, active, corrupt, unjournalled and foreign objects all remain recoverable/untouched.
                // Do not diagnose them as deleted, and do not log paths or journal payloads.
                retained++;
            }
        }
        return new(inspected, removed, retained, Limited: names.Length > maximumEntries);
    }

    internal static bool IsMissingRecoveryRoot(IOException exception)
        => exception.InnerException is Win32Exception { NativeErrorCode: 2 or 3 };
}
