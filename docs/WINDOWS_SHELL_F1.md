# Windows Shell drag-and-drop verification

## Scope and limitations

This audit builds on F0 and the existing Shell adapter. Explorer acceptance is NOT TESTED until a person runs the scenarios below on an interactive Windows desktop. The GitHub-hosted runner and COM tests do not establish Explorer compatibility or successful network transfers. `IStream.Clone` currently reports unsupported and may prevent compatibility with consumers that require it. The staging cache limits a drag to 16 GiB in total and stages the complete file before exposing it as a stream; disk space and transfer time must be available. An individual file larger than 4 GiB is representable by the descriptor but has not been tested end to end.

Microsoft contracts reviewed: [Shell formats and lindex](https://learn.microsoft.com/en-us/windows/win32/shell/clipboard), [FILEDESCRIPTORW](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/ns-shlobj_core-filedescriptorw), [ISequentialStream::Read](https://learn.microsoft.com/en-us/windows/win32/api/objidl/nf-objidl-isequentialstream-read), [IStream::Clone](https://learn.microsoft.com/en-us/windows/win32/api/objidl/nf-objidl-istream-clone) and [ReleaseStgMedium](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-releasestgmedium).

The descriptor uses a DWORD count followed by 592-byte Unicode records, with file size split into high and low DWORDs, paths terminated by the zero-initialized buffer, and explicit directory attributes. The planner rejects invalid names and unsupported entries. FileContents uses the descriptor index, and the enumeration now lists each regular file at its descriptor index. A native vtable test checks Read's HRESULT. These tests do not prove Explorer's handling of empty directories, shell apartments, or error UI.

## Prepare a Windows 11 test

1. Download the PR's **Teleclav-portable-win-x64** Actions artifact and extract the artifact and its inner ZIP into a new folder. Verify the supplied SHA-256 file. Install Microsoft WebView2 Evergreen Runtime if missing; no .NET SDK is needed on the test computer. Launch `Heimdall.exe`.
2. Connect to a disposable Ubuntu OpenSSH SFTP test account with access to a test folder. Keep source originals. Prepare small files with distinct contents, names with spaces and Unicode, two folders, nested folders, at least one empty folder, and an empty file. For the >4 GiB test use a separately prepared file only if enough disk and bandwidth are available.
3. In Explorer make empty destination folders on the Desktop, Downloads, a different ordinary folder, and another local drive if one is available. Keep them separate for each case. Avoid overwriting existing files.
4. Drag from the remote panel into each destination. Record the specific combination and whether Explorer accepts the drop, creates the expected tree, reports errors, and finishes. Compare each file's size and SHA-256 with the source; ensure empty directories exist. On Windows use `Get-FileHash -Algorithm SHA256 -LiteralPath 'C:\path\to\file'`; compare with `sha256sum` run by the tester on the Ubuntu host. These commands are only for independent verification, not application dependencies.

Test a single file; several files in one selection; one directory; several directories; nested and empty directories; a mixed file and directory selection; Unicode and spaces; the empty file; and a >4 GiB file. Repeat across Desktop, Downloads, an arbitrary directory, and another drive. Include a selection whose first item is a directory and subsequent items are files, as well as a destination that refuses the drop. Test remote-to-remote and remote-to-local internal panel operations separately after a directory tree has been prepared. Record actual results individually; an unavailable destination is NOT TESTED.

For cancellation and transfer errors, use disposable copies and deliberately disconnect the test SFTP session during a transfer. Record the Explorer result, any partial destination file and application warning; do not assume Explorer removes a partial file. Do not test by overwriting important data.

## Build and evidence

Use the self-contained Windows build and locked restore commands in [BUILDING.md](BUILDING.md). The normal CI publishes its own ZIP and test results. The F1 probe run records native COM and Shell regression tests. Results and exact head are recorded in the PR description because a commit cannot include its own final SHA or its future CI result.
