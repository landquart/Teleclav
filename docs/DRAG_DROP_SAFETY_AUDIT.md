# D4: reverse drag-and-drop safety audit (incomplete)

Audit date: 2026-10-30. Base: D3 `d8358e6c044901ae2977f3790152406f0043e886` (draft PR #6). This is a static, partial audit, not a release certification. No D4 code fix or regression test has been executed. D1–D3 themselves remain unverified with Explorer and a real SFTP server. Do not merge on the strength of this document.

## Data flow and failure map

| Stage | Ownership and behavior observed in code | Failure / open question |
|---|---|---|
| SFTP listing | `ListDirectoryStrictAsync` serializes on `_clientLock`, checks path components before and after listing; rejects unsafe names and over 10,000 entries. | Checks are not atomic; listing can race with path replacement. Server omissions cannot necessarily be detected. Network calls and their cancellation need live testing. |
| Tree plan | `ShellFolderPlanner` uses `RemoteDownloadTreePlanner`; unsupported entries and unsafe paths fail the Shell plan; depth, path and entry limits apply. | Directory contents can change after planning; a plan is not a snapshot. Invalid metadata and collisions need hostile-server tests. |
| Shell descriptors | `ShellVirtualFileDescriptor` emits names, sizes and directory entries; `ShellVirtualFileDrag` supplies indexed streams. | Explorer controls what it requests and writes. Handling of its failures, collisions and directory creation is unverified. |
| Staging and read | `ShellDownloadCache` reserves up to 16 GiB; each item is downloaded once to a temporary directory. `DownloadFileWithinLimitAsync` writes a uniquely named `CreateNew` partial file, checks sizes and visible attributes, then moves it into the cache without overwrite. Readers hold leases; cleanup waits for downloads and readers. | Read/network/disk errors propagate, but Explorer's resulting local files and UI are unverified. The cache's per-file names are predictable inside a random directory. No explicit restrictive Windows ACL or reparse-point protection is established here. |
| COM and destination | `GetData` blocks on cache completion and returns `IStream`. Lifetime waits for both drag return and async `EndOperation`. `ShellReadStream` leases persist until disposal/finalization. | Whether Explorer calls `EndOperation`, how COM releases managed streams, concurrency, responsiveness and memory retention require Windows testing. Destination writes and their atomicity belong to Explorer. |

## Findings and residual risks

1. **TOCTOU / untrusted server:** `EnsureDirectoryComponents` and `GuardedSftpDownload` perform separate path checks and `OpenRead`. Replacement between checks and open, including a temporary symlink restored before subsequent checks, can escape the selected tree or select different content. Matching attributes and FSTAT size do not establish identity or immutable contents. Public SSH.NET API as investigated in D3 does not provide an atomic no-follow open of each path component or a verified full handle identity. Treat remote hosts and concurrently writable trees as untrusted for this guarantee; no safe guarantee is claimed. D3's checks reduce detectable cases, not the race. Server-side isolation or a protocol/implementation with suitable handle-relative no-follow semantics must be evaluated before claiming a fix.
2. **Sensitive staging:** `Directory.CreateTempSubdirectory` creates a randomized directory but this code does not explicitly configure or verify a restrictive Windows ACL; cleanup is best effort. Crash, failure to delete, or retained COM references may leave sensitive data. No stale cleanup exists. A safe cleanup design must avoid deleting active sessions and defend against reparse-point substitution; blindly deleting old directories is unsafe. Executability is not prevented by this code. These are unresolved security requirements.
3. **Cancellation / liveness:** `GetData` passes `CancellationToken.None` to the cache and synchronously waits. Cache disposal cancels its own token, but cleanup waits for `EndOperation` when async extraction has begun. If a consumer fails to end the operation, the retained COM reference and cache may survive indefinitely. A stalled synchronous read observes cancellation only around calls; the configured SFTP exchange timeout is 15 minutes, not a bound on the whole operation. Explorer cancel, Escape, tab/app close, and forced disconnect are not proven to stop every operation. A timeout must not free an object while a consumer still owns it.
4. **Final destination and partial batches:** the temporary-file commit applies only to Teleclav's staging, not the destination written by Explorer. Explorer may create or overwrite files, leave partial files, or complete part of a tree. This code cannot certify an atomic batch or independently confirm that every destination file is durable and correct. Failure of an item must not be interpreted as full success; actual Explorer behavior and reporting are untested.
5. **Privacy:** `LoggingRemoteBrowser.DownloadFileWithinLimitAsync` records remote and local paths, sizes and operation status when session logging is enabled. Paths may themselves be sensitive. The inspected transfer method does not log contents or credentials, but this is not a complete privacy or telemetry audit. `ShowOperationWarning(ex.Message)` displays exception text; whether all possible server-provided errors are sanitized is unverified. Rollback logs the temporary path and exception.
6. **Lifetime and memory:** lease cleanup is synchronized and idempotent in the examined paths, but finalizer timing is nondeterministic; `ShellReadStream`'s disposal flag is not synchronized. Whether concurrent COM calls or disposal cause races is unverified. No real repeated-drag stress or process memory measurement was run.
7. **Change during read:** different size/visible attributes are rejected when observed. Same-size changes with preserved or restored timestamps, and changes after a check, may pass; there is no snapshot or verified source hash. A successful staged read is not proof of the originally planned state.

## Scenario register

| Scenario | Status and evidence |
|---|---|
| Existing destination, name conflicts, no disk space or permissions, nested directory failure | NOT RUN with Explorer; final destination is Explorer-owned. |
| Remote read error, disconnect, timeout, server shutdown, reconnect | NOT RUN end-to-end. Static code propagates exceptions; no unbounded retry was found in inspected transfer path. A short, bounded local move retry exists. |
| Cancellation before drop / Escape / target refusal | NOT RUN. Folder preparation observes mouse release and view disposal; actual drag/COM behavior needs Windows. |
| Cancellation during transfer / close tab or application / SFTP disconnect | NOT RUN. Cancellation and lifetime limitations described above; no guaranteed timely termination. |
| Partial tree and errors | NOT RUN. Explorer may leave previously written entries. |
| Malicious names, links, special entries, changing paths | Existing code rejects detected invalid entries; TOCTOU remains. No hostile-server tests run in this audit. |
| Temporary directory access, crash cleanup, reparse substitution | NOT RUN; implementation has no explicit ACL or stale cleanup. |
| Concurrent/repeated drags, COM releases, STA responsiveness and memory | NOT RUN on Windows. |
| Password/key/content logging and telemetry | Partial static inspection only; whole application and exception text not audited. |
| D1–D3 and Windows → Ubuntu regression | NOT RUN. PR #3 remains separate and unmerged. `git merge-tree` showed no textual conflict between its head and D3; this is not behavioral compatibility. |

## Tests, fixes, and release blockers

No D4 behavior was changed; no new tests or fixes are claimed. The SDK 10.0.103 executable ran on Linux, but its installer reported extraction ownership errors and the SFTP test restore failed in MSBuild before tests, with zero reported errors; Windows Desktop Runtime was unavailable. Crapper, Dryer and Mutator were not available. No CRAP, duplication or mutation metrics were obtained. No real Windows/Ubuntu environment was available.

Before treating D4 as complete: design and test restricted staging and crash recovery; resolve or explicitly prevent unsafe use where no-follow guarantees cannot be obtained; implement bounded cancellation and correct ownership without breaking COM clients; run adversarial SFTP tests and Windows Explorer end-to-end tests for every scenario above, verify hashes and partial results, measure repeated-drag memory, and run D1–D3 plus upload regression with PR #3 applied separately. Capture red/green evidence for behavior fixes. Evaluate all findings rather than assuming CI proves safety.
