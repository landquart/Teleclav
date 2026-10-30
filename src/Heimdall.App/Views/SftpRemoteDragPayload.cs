/*
 * Copyright 2026 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Windows;
using Heimdall.App.ViewModels;
using Heimdall.Sftp;

namespace Heimdall.App.Views;

/// <summary>
/// What a drag out of the remote file list carries: the entries and the pane they come from.
/// </summary>
/// <remarks>
/// The payload remains inside the application. For a single regular file the Shell adapter also
/// advertises virtual-file formats externally; this marker preserves internal moves and downloads.
/// For other selections the original application-only drag object is used.
/// </remarks>
/// <param name="Source">The view model of the pane the drag started in.</param>
/// <param name="Entries">The dragged remote entries.</param>
public sealed record SftpRemoteDragPayload(
    EmbeddedSftpViewModel Source,
    IReadOnlyList<SftpFileInfo> Entries)
{
    // A Shell drag carries the same marker through COM; its managed source remains local.
    [ThreadStatic]
    internal static SftpRemoteDragPayload? ActiveShellDrag;

    /// <summary>The data format name the payload is stored under.</summary>
    public const string FormatName = "Heimdall.SftpRemoteEntries";

    /// <summary>Reads the payload from a drag, or <see langword="null"/> when the drag carries none.</summary>
    public static SftpRemoteDragPayload? From(System.Windows.IDataObject? data)
        => data is not null && data.GetDataPresent(FormatName)
            ? ActiveShellDrag ?? data.GetData(FormatName) as SftpRemoteDragPayload
            : null;
}
