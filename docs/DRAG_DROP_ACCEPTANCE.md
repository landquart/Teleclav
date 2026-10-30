# Drag & Drop acceptance - D5 draft

Status: **NOT TESTED end-to-end; not release-ready**. Date: 2026-10-30. All functional desktop rows below require Windows 11 x64, Explorer, and a dedicated Ubuntu OpenSSH SFTP account. No interactive Windows desktop or Ubuntu OpenSSH is available in this environment. Use only disposable test data, not secrets or irreplaceable files.

## Git inputs and CI

`master` was `1959e57c7351d73f1d349b606b19bf6090273db9`. PRs #2-#7 are open drafts, not merged. Their head and base commits:

| PR | Topic | Base | Head |
|---|---|---|---|
| #2 | Windows CI/portable | `1959e57c7351d73f1d349b606b19bf6090273db9` | `3d02863678d76ad79f507df77ad3db88cbc22658` |
| #3 | incomplete upload | `1959e57c7351d73f1d349b606b19bf6090273db9` | `50bb98524e20a84adb62a1dfdbd6978cb0b4cf31` |
| #4 | D1 | `1959e57c7351d73f1d349b606b19bf6090273db9` | `d87faf5b81fb6789b14c6cdfca31d965b4a5566a` |
| #5 | D2 | `d87faf5b81fb6789b14c6cdfca31d965b4a5566a` | `8b1b22448924dbe32549c9c0e0329cbd7d8dd864` |
| #6 | D3 | `8b1b22448924dbe32549c9c0e0329cbd7d8dd864` | `d8358e6c044901ae2977f3790152406f0043e886` |
| #7 | D4 | `d8358e6c044901ae2977f3790152406f0043e886` | `0bbed929a4be9a472e139f7806747653f90fd2dc` |

D5 applies the reviewed patches of #2 and #3 to the #7 tree in a separate branch, without merging those PRs. The #2 and #3 changed-file sets do not intersect the D1-D4 changed-file set; D1-D4 intentionally overlap in their stacked Shell/SFTP files. No textual conflict arose when applying #2 and #3. No review submissions or inline review threads were returned for #2-#7 at inspection. The GitHub connector returned no PR workflow runs or commit statuses for their listed heads. This does not establish why CI did not run. See the file inventories below.

The portable workflow is inherited from #2 and requests read-only contents permission, Windows `windows-2025-vs2026`, SDK 10, Release `win-x64` self-contained .NET publish, ZIP and SHA-256 as a 14-day Actions artifact. It uses no publication secret or paid signing. Workflow execution and Windows launch are **NOT TESTED**. WebView2 Evergreen Runtime is still required separately. A Linux cross-publish uses a non-locked restore due Linux RID and is not proof of the Windows workflow.

## Functional matrix

All rows below are **NOT TESTED** on real Windows/Ubuntu, unless specified otherwise. A pass requires comparing the actual tree, relative paths, file and folder counts, sizes and SHA-256. Preserve the source and record which items completed when a transfer is cancelled or fails.

| Direction or subsystem | Scenarios | Status |
|---|---|---|
| Windows → Ubuntu | single file; multiple files; one folder; multiple folders; nesting; empty folders; mixed selection; Unicode; large files; name conflicts; cancellation | NOT TESTED |
| Ubuntu → Windows Explorer | same eleven scenarios | NOT TESTED |
| Ubuntu → Explorer destinations | Desktop; Downloads; arbitrary local folder; second drive | NOT TESTED |
| Ubuntu → Explorer selection and recovery | different remote directories; repeated drag; disconnect and controlled retry | NOT TESTED |
| Internal drag | local panel → SFTP; SFTP → local panel; movement within remote browser | NOT TESTED |
| SSH | connect; execute command; switch terminal/SFTP; transfer while terminal active; disconnect and reconnect | NOT TESTED |
| Failure safety | existing destination; conflict choices; disk full; no write rights; read error; disconnect; timeout; nested folder error; interruption after part of tree | NOT TESTED in Explorer |
| Cancellation / lifetime | before drop; Escape; destination refuses; during transfer; tab close; app close; SFTP disconnect; SSH session termination; repeated drags/COM/memory | NOT TESTED in Explorer |
| Hostile or changed source | invalid names; links; special objects; size/timestamp changes; same-size change; path replacement | NOT TESTED against hostile Ubuntu server; deterministic mock demonstrates same-size substitution can pass |
| Staging | Windows ACL and deny-execute; stale cleanup; crash; active-reader retention; parent reparse attacks | NOT TESTED on Windows; stale cleanup and complete parent-path protection not implemented |
| Log privacy / telemetry | credentials, keys, file contents, paths, exception text, outbound telemetry | NOT TESTED end-to-end; partial static inspection only |

## Reproducible fixture and hashes

The fixture generator uses Python 3 on Ubuntu. It refuses an existing destination. The manifest must live outside the transferred tree. Example (replace paths only with dedicated test directories):

```bash
python3 scripts/drag_drop_fixture.py create /tmp/teleclav-fixture --large-mib 128
python3 scripts/drag_drop_fixture.py manifest /tmp/teleclav-fixture /tmp/teleclav-expected.json
```

It creates regular text and binary files, a zero-byte file, Unicode names, nested and empty directories and a reproducible 128 MiB binary file. Use `--large-mib 4097` for a file larger than 4 GiB, only if both sides have enough space; the D4 cache has a 16 GiB limit. Copy the expected JSON separately to Windows. After dragging the fixture to an empty Windows target, run in Windows PowerShell from the extracted test-tools directory:

```powershell
.\Compare-DragDropManifest.ps1 -Root 'C:\path\teleclav-fixture' -Manifest 'C:\path\teleclav-expected.json'
```

This compares exact relative paths, counts, each file size and SHA-256, including empty directories. The PowerShell comparator has not been executed on Windows in this environment. To test the opposite direction, first copy the fixture to Windows, drag it from Explorer or the local panel to an empty Ubuntu directory, then on Ubuntu run:

```bash
python3 scripts/drag_drop_fixture.py verify /tmp/destination/teleclav-fixture /tmp/teleclav-expected.json
```

Repeat the matrix with fresh empty destination directories. For conflicts, first place a different file at the destination, observe Explorer/application conflict options, and verify the outcome and original data after each choice. For interruption, retain the source, interrupt partway through a large transfer, inspect both destination and temporary files and note any partially completed items. Do not infer success merely from file sizes. If several remote sources are in different directories, use separate drags unless the UI actually supports cross-directory selection.

## D5 automated checks on the combined tree

- PASS: seven Python infrastructure/fixture tests; repository reference, notice and vendored terminal hash check.
- PASS: 16 source-linked Linux xUnit tests for cache, streams, error mapping and transfer registry; the logger is stubbed. Not COM or WPF execution.
- FAIL: full SFTP suite on Linux: 572 passed, two failed when attempting to start Windows `cmd.exe`, one integration test skipped without a server. No tests were disabled.
- FAIL: full Core suite on Linux: 4,758 passed, 195 failed; observed failures include Windows principal and `shell32.dll` dependencies. The failures have not all been individually classified.
- BLOCKED: SSH test project compiled for Windows, but execution aborted before tests because `Microsoft.WindowsDesktop.App` is unavailable on Linux.
- PASS: integrated Windows application/test project cross-build and Release `win-x64` self-contained .NET cross-publish on Linux, zero build errors and warnings. NOT TESTED: running the executable on Windows, WebView2, Explorer and the GitHub Windows runner.
- FAIL: Linux `--locked-mode` restore reported NU1004 for Linux/Windows runtime differences; the local cross-publish used unlocked restore, with lock-file changes excluded from the PR. Windows locked-mode restore remains NOT TESTED.
- Fixture generator tests: first failed because implementation was absent, then passed; the independent PowerShell comparator is NOT TESTED on Windows.

## Automation and current risks

Prior D4: 42 targeted tests passed with a local Paramiko SFTP server and 16 source-linked Linux tests passed; these do not verify Windows COM. PR #3's selected upload/planner tests passed in an isolated Linux integration checkout; Windows upload remains untested. Prior full SFTP run had 568 passes, two Linux failures due to absent `cmd.exe`, and one skipped server test. These are historical branch results, not evidence for the integrated D5 commit.

The original SFTP path checks do not eliminate TOCTOU: a server or another writer may replace a file or directory between checks and open, and matching metadata cannot prove identical content. Server-side isolation is needed to bound this risk. Explorer writes final files and may leave partial results; staging does not make destination writes atomic. Cache limit is 16 GiB and a per-file wait is bounded at 24 hours; a non-cooperative background read or missing COM `EndOperation` can still retain resources. Crash cleanup is not implemented. Do not use this draft with sensitive or irreplaceable data.

Crapper, Dryer and Mutator upstream describe support for Clojure, Java, Go, TypeScript, Rust and Python, not C#; their metrics are unavailable. Do not substitute a green build for desktop testing.

## Changed files by input PR


PR #2:

- `.github/workflows/ci.yml`
- `docs/BUILDING.md`
- `scripts/check_repository_integrity.py`
- `tests/infrastructure/test_repository_integrity.py`

PR #3:

- `locales/en.json`
- `locales/es.json`
- `locales/fr.json`
- `src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.Transfers.cs`
- `src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs`
- `tests/Heimdall.App.Tests/EmbeddedSftpUploadConflictTests.cs`
- `tests/Heimdall.Sftp.Tests/RemoteUploadTreePlannerTests.cs`

PR #4:

- `src/Heimdall.App/Views/EmbeddedSftpView.xaml.cs`
- `src/Heimdall.App/Views/SftpRemoteDragPayload.cs`
- `src/Heimdall.App/Views/ShellReadStream.cs`
- `src/Heimdall.App/Views/ShellVirtualFileDescriptor.cs`
- `src/Heimdall.App/Views/ShellVirtualFileDrag.cs`
- `tests/Heimdall.App.Tests/ShellVirtualFileDescriptorTests.cs`
- `tests/Heimdall.App.Tests/ShellVirtualFileDragTests.cs`

PR #5:

- `src/Heimdall.App/Services/LoggingRemoteBrowser.cs`
- `src/Heimdall.App/Views/EmbeddedSftpView.xaml.cs`
- `src/Heimdall.App/Views/ShellAsyncLifetime.cs`
- `src/Heimdall.App/Views/ShellDownloadCache.cs`
- `src/Heimdall.App/Views/ShellReadStream.cs`
- `src/Heimdall.App/Views/ShellVirtualFileDescriptor.cs`
- `src/Heimdall.App/Views/ShellVirtualFileDrag.cs`
- `src/Heimdall.Sftp/IRemoteBrowser.cs`
- `src/Heimdall.Sftp/LimitedWriteStream.cs`
- `src/Heimdall.Sftp/SftpBrowser.cs`
- `tests/Heimdall.App.Tests/ShellAsyncLifetimeTests.cs`
- `tests/Heimdall.App.Tests/ShellDownloadCacheTests.cs`
- `tests/Heimdall.App.Tests/ShellVirtualFileDescriptorTests.cs`
- `tests/Heimdall.App.Tests/ShellVirtualFileDragTests.cs`
- `tests/Heimdall.Sftp.Tests/LimitedWriteStreamTests.cs`

PR #6:

- `src/Heimdall.App/Services/LoggingRemoteBrowser.cs`
- `src/Heimdall.App/Views/EmbeddedSftpView.xaml.cs`
- `src/Heimdall.App/Views/ShellVirtualFileDescriptor.cs`
- `src/Heimdall.App/Views/ShellVirtualFileDrag.cs`
- `src/Heimdall.Sftp/GuardedSftpDownload.cs`
- `src/Heimdall.Sftp/IRemoteBrowser.cs`
- `src/Heimdall.Sftp/SftpBrowser.cs`
- `src/Heimdall.Sftp/SftpPathGuard.cs`
- `src/Heimdall.Sftp/ShellFolderPlanner.cs`
- `src/Heimdall.Sftp/WindowsFileName.cs`
- `tests/Heimdall.App.Tests/ShellVirtualFileDragTests.cs`
- `tests/Heimdall.Sftp.Tests/GuardedSftpDownloadTests.cs`
- `tests/Heimdall.Sftp.Tests/SftpPathGuardTests.cs`
- `tests/Heimdall.Sftp.Tests/ShellFolderPlannerTests.cs`
- `tests/Heimdall.Sftp.Tests/ShellFolderSftpIntegrationTests.cs`

PR #7:

- `docs/DRAG_DROP_SAFETY_AUDIT.md`
- `src/Heimdall.App/Views/EmbeddedSftpView.xaml.cs`
- `src/Heimdall.App/Views/ShellDownloadCache.cs`
- `src/Heimdall.App/Views/ShellReadStream.cs`
- `src/Heimdall.App/Views/ShellStagingDirectory.cs`
- `src/Heimdall.App/Views/ShellTransferFailure.cs`
- `src/Heimdall.App/Views/ShellTransferRegistry.cs`
- `src/Heimdall.App/Views/ShellVirtualFileDrag.cs`
- `tests/Heimdall.App.Tests/ShellDownloadCacheTests.cs`
- `tests/Heimdall.App.Tests/ShellReadStreamTests.cs`
- `tests/Heimdall.App.Tests/ShellStagingDirectoryTests.cs`
- `tests/Heimdall.App.Tests/ShellTransferFailureTests.cs`
- `tests/Heimdall.App.Tests/ShellTransferRegistryTests.cs`
- `tests/Heimdall.App.Tests/ShellVirtualFileDragTests.cs`
- `tests/Heimdall.Sftp.Tests/GuardedSftpDownloadTests.cs`
