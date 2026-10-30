/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Text;

namespace Heimdall.Sftp;

/// <summary>One item in the virtual Shell hierarchy, with a slash-separated relative path.</summary>
public sealed record ShellFolderItem(RemoteDownloadOpKind Kind, string RemotePath, string RelativePath, long Size);

/// <summary>Plans a complete representable tree without following links or silently dropping entries.</summary>
public static class ShellFolderPlanner
{
    private const int MaximumItems = 10000;
    private const string Root = "root";

    public static async Task<IReadOnlyList<ShellFolderItem>> PlanAsync(
        IReadOnlyList<SftpFileInfo> roots,
        Func<string, CancellationToken, Task<IReadOnlyList<SftpFileInfo>>> list,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(list);
        if (roots.Count == 0) { throw new ArgumentException("The selection is empty.", nameof(roots)); }
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (root is null || !root.FullPath.StartsWith('/')
                || root.FullPath.Split('/').Skip(1).Any(part => part.Length == 0 || part is "." or "..")
                || !root.FullPath.EndsWith('/' + root.Name, StringComparison.Ordinal))
                throw new IOException("A selected path does not match its name.");
        }

        async Task<IReadOnlyList<SftpFileInfo>> ListChecked(string parent, CancellationToken token)
        {
            var children = await list(parent, token).ConfigureAwait(false);
            string prefix = parent.TrimEnd('/') + "/";
            foreach (var child in children)
            {
                token.ThrowIfCancellationRequested();
                if (child is null || child.FullPath != prefix + child.Name)
                    throw new IOException("A remote entry is outside its listed directory.");
            }
            return children;
        }

        string? Resolve(string parent, string name)
        {
            if (!WindowsFileName.IsSafeName(name)) { return null; }
            string relative = parent == Root ? name : parent[(Root.Length + 1)..] + "/" + name;
            if (relative.Length >= 260 || !used.Add(relative.Normalize(NormalizationForm.FormC))) { return null; }
            return Root + "/" + relative;
        }

        RemoteDownloadPlan plan = await RemoteDownloadTreePlanner.PlanAsync(
            roots, Root, ListChecked, Resolve,
            count => { if (count > MaximumItems) throw new IOException("The folder contains too many entries for a Shell transfer."); }, ct).ConfigureAwait(false);
        if (plan.SkippedUnsupportedPaths.Count != 0)
            throw new IOException($"Unsupported remote item: {plan.SkippedUnsupportedPaths[0]}");
        if (plan.SkippedUnsafeNames.Count != 0)
            throw new IOException($"Unsafe or conflicting Windows name: {plan.SkippedUnsafeNames[0]}");
        return plan.Ops.Select(op => new ShellFolderItem(op.Kind, op.RemotePath,
            op.LocalPath[(Root.Length + 1)..], op.Size)).ToArray();
    }
}
