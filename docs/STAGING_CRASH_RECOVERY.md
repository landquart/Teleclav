# Staging crash recovery

This document describes the F3.4 implementation in PR #12. The scope is local
Windows Shell staging for reverse SFTP drag-and-drop. Recovery abandons an
interrupted transfer and cleans verified temporary objects; it does not resume
downloads or report them as successful. See [the security audit](DRAG_DROP_SAFETY_AUDIT.md)
for the F3.1-F3.3 protocol and native ABI review.

## Ownership and trust boundary

The per-user root is `%LOCALAPPDATA%\Teleclav\Staging` on a fixed local NTFS
volume. The two application directories have protected ACLs. Creation and recovery
pin the component chain with directory handles that omit delete sharing, reject
reparse points, and validate owners/ACLs. Recovery opens existing directories only;
it neither creates a missing root nor repairs unexpected permissions.

Each operation has a random GUID directory and an exclusive `.teleclav-owner`
journal. Its header binds the format version, operation GUID, root and operation
volume serials and 128-bit file IDs. File records bind a numeric cache target,
object GUID, expected size, protocol state, optional file ID and publication name.
The flushed creation intent precedes exclusive relative creation of the partial
file, whose ownership EA is supplied in the same native request. The EA bridges
the crash window before the identity record is flushed. Journal hash chains detect
corruption; the protected namespace supplies trust. Neither hashes nor EAs are
authentication against a hostile process running as the same account.

The account owner, Administrators, SYSTEM, a compromised OS and a compromised
initial drive namespace remain outside the protection boundary. No secure erasure
or absolute persistence across power loss/controller failure is claimed. Namespace
creation, journal flushing and file publication are not one filesystem transaction.

## Interprocess coordination

The journal is opened with read/write/delete access and share mode zero. That
same handle is retained throughout a live operation or recovery. The journal
itself must be regular, have one hard link, trusted permissions and the expected
identity. This handle is both the writer lock and the exclusive recovery lease.
There is no separate observation of a PID followed by a later lock acquisition.

| Situation | Result |
| --- | --- |
| Another instance owns the journal | Exclusive open fails; leave the entire operation in place. |
| Two recovery processes race | At most one holds the journal lease; a competing directory open can temporarily delay deletion, so a later pass may be needed. |
| Owner is terminated | Windows closes its handles; a new process can obtain the lease and must still validate all persisted identities and states. |
| A process has another settings profile | It shares this per-user staging root and follows the same journal lease protocol. |
| PID changes or is reused | No decision depends on a PID, process name, timestamp or directory age. |
| Another transfer starts | It exclusively creates a new GUID operation; it cannot adopt an existing operation directory. |

Discovery pins the root, takes a bounded name snapshot, then reopens each
candidate through the checked component chain. The reopened root identity must
equal the discovery identity. Acquiring the lease never substitutes for checking
the directory header, ACLs, type, volume, file identities and creation tags.

## Recovery state transitions

All visible children are preflighted before any data deletion. Candidates are
opened relative to the pinned operation handle, with no-follow semantics and
exclusive sharing. Deletion uses the same verified handle. Unknown entries or a
changed identity refuse cleanup; there is no recursive deletion of an unknown tree.

| Durable state / filesystem observation | Safe action |
| --- | --- |
| Directory created before a valid journal exists | Retain, including an empty directory; no persisted identity authorizes recovery. |
| Valid header, no file intent | Remove the verified empty operation through its retained handle. |
| Reserved intent, no file | Reconcile absence, persist deletion state, finish cleanup. |
| Reserved intent, matching EA-tagged partial without recorded ID | Validate type, volume, single link, ACL and exact tag; bind its observed identity and delete through that handle. |
| Created / Writing / Written partial | Validate recorded identity and tag; abandon and delete, regardless of incomplete contents. |
| Publishing | Either the partial name or final name may exist; validate the original identity/tag; refuse two names claiming one object. |
| Published cache file | Validate identity/tag, then delete if no incompatible open handle remains. |
| Open reader | Refuse incompatible sharing; keep journal and files for a later attempt. Never force-close the reader. |
| Deleting after a crash | Reconcile an absent file or delete the matching original object; refuse a reused name. |
| Deleted but name reappears | Treat it as unknown; do not delete it. |
| Missing registered file | Persist reconciled deletion; do not infer ownership of another name. |
| Invalid header/version/complete frame/hash/transition or conflicting IDs | Retain the operation; continue scanning other operations. |
| Bounded torn final frame after a valid prefix | Truncate only the incomplete tail, flush the verified prefix, replay it and still require object validation. |
| Unknown child, changed ID, junction, symlink, reparse point or hard link | Refuse; preserve the child and journal. |
| Journal already unlinked before final directory close, then process dies | Retain the empty unjournalled residue; a name or emptiness does not grant deletion authority. |

For v1 journals, only the original registered file identities authorize deletion;
unregistered v1 partials remain unproven. Unsupported formats are not repaired or
silently upgraded. A damaged operation cannot authorize cleanup of its neighbors.

## Startup, retries and diagnostics

After the application's single-instance decision, startup schedules recovery off
the WPF dispatcher. It does not wait for recovery before showing the application.
The journal lease is independent of the single-instance setting. Privilege-helper
and suppressed test startup paths do not run the product recovery task.

There are at most three passes, two seconds between passes, at most 128 processed
root entries per pass (one extra entry detects truncation), and a five-second
cooperative budget per pass. Native synchronous driver calls and flushes cannot
be forcibly interrupted: this is not a hard wall-clock upper bound. Application
exit cancels future work and does not wait for or forcibly close active streams.
The journal is bounded to 16 MiB. Cancellation during deletion leaves replayable
progress; a budget stop is diagnosed as incomplete, never successful completion.

Locks, access denial, unknown objects and corruption count as retained entries.
Diagnostics contain counts/limit status or a fixed warning, without file paths,
contents, journal payloads or credentials. If recovery refuses the root, it leaves
the root untouched. A per-operation failure does not abort recovery of valid peers.

An Explorer, antivirus or indexer handle with incompatible sharing prevents
deletion. A subsequent bounded pass can succeed after it closes. After all three
passes, no permanent polling task remains; the next launch can retry. After a
Windows restart, process handles are released, but validation remains mandatory
and persisted corruption can still require manual handling. Ordinary cache cleanup
also retains its own finite retry schedule; it does not evict a live COM stream.

More than 128 persistently unrecognized entries early in enumeration can prevent
later candidates from being visited in these bounded passes. No eventual-drain
guarantee is made for an adversarial or unbounded root. Recovery warnings must be
investigated rather than interpreted as proof that the temporary area is empty.

## Old temporary directories and manual handling

Older `teleclav-drag-*` directories in `%TEMP%` without this journal are not scanned
or automatically deleted. A prefix, an old timestamp, a PID or a marker file is
insufficient evidence. Unjournalled directories in the new root are also retained.

When a warning persists:

1. Finish all drags, close Teleclav instances and the Explorer windows consuming
   their streams. Wait for other programs' file handles to close; do not force
   removal while a consumer still uses the files.
2. Inspect only the specific retained directory with a file manager that displays
   junction/symlink targets. Establish independently that its contents belong to
   your abandoned transfers. Keep anything whose origin is uncertain.
3. Back up uncertain data outside staging. Do not edit a journal, change ACLs or
   fabricate tags to make automatic recovery accept an object.
4. If you positively identify disposable files, remove only those selected files
   and then the empty directory. Do not run a recursive wildcard cleanup by prefix
   and do not follow links into other directories. If access is denied, identify
   the blocker instead of escalating privileges for a blanket deletion.

Manual identification is a user decision, not a guarantee supplied by recovery.
Legacy data may remain until that decision; this is not supported crash recovery
for the pre-journal format.

## Verification and reproduction

Run on Windows with the repository's pinned SDK:

```powershell
dotnet restore Heimdall.slnx --locked-mode
dotnet build Heimdall.slnx --no-restore
dotnet test tests/Heimdall.App.Tests/Heimdall.App.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~ShellStaging|FullyQualifiedName~ShellDownloadCache" --logger "trx;LogFileName=staging-ownership.trx" --results-directory TestResults
```

`Heimdall.StagingCrashStub` is a separate source-linked test executable. Production
has no environment-variable crash switch. The existing numeric modes call
`Environment.Exit(37)` at protocol boundaries without unwinding `using`/`finally`.
External-termination modes instead flush a readiness signal while the critical
handles are still live, then the parent calls `Process.Kill(entireProcessTree: true)`.
A readiness failure is a failed test; fixture disposal killing a stuck process
is never counted as successful crash coverage.

The publication-intent boundary stops after the Publishing record is flushed but
before rename. Together with the post-rename boundary, this exercises both legal
names for a file interrupted during publication.

The scanner fixture source-links the product retry scheduler, including its
bounded sharing-refusal retries. The nonempty-partial case explicitly flushes
the fixture's first two bytes before signaling readiness; other crash points can
legitimately lose bytes still in the managed FileStream buffer. This does not add
per-write flushing to production downloads or claim power-loss durability.

Windows CI 73 on `b88bc1c5` ran 171 security/cache cases: 169 passed and two new
fixture checks failed. The partial-content assertion observed an empty file after
unflushed buffered writes, and immediate publication-state inspection encountered
a sharing refusal. The fixture now flushes its deliberate partial fragment, scans
through the product scheduler and bounds inspection retries to sharing errors
32/33 only. Content, protocol-state, foreign-data and subsequent-transfer assertions
remain required; persistent refusal is still a test failure.

CI 74 on `a35c257f` passed all 171 Debug staging/cache cases. The full Release
suite reported 19,389 passed, one failed and one skipped; the only failure was
DuringWrite fixture teardown reporting a sharing refusal on the journal. That
finally exception could hide an earlier inspection error, so it is not evidence
of successful recovery or proof of a specific earlier failure.

The inspected premise was that waiting for a killed process made path access
immediately available. There are three distinct actors: the terminated writer,
the inspecting test and fixture teardown. Inspection now first acquires the
validated exclusive journal lease, bounds sharing-only file-read retries, and
releases that lease before the fresh scanner starts. Both assertion and teardown
exceptions are preserved if they fail together. This changes the test's ownership
handoff and diagnostics; production deletion authority and assertions are not
relaxed. Final verification results are recorded in PR #12.

CI 75 on `e865c556` again passed 171 Debug cases, and DuringWrite passed in the
full Release suite. Three damaged-operation fixture cases failed before their
expected multi-pass scan completed, with teardown sharing errors masking setup
errors. These cases had still mutated files/directories immediately after the
process wait. Their setup now reacquires the validated journal lease and an
exclusive data-file handle, verifies the published identity and original contents,
then closes those handles before injecting corruption/substitution. The partial
inspection likewise reads through its exclusive data handle. Both fixture families
preserve simultaneous body/teardown failures. Tests intentionally retaining an
independent external reader do not use this exclusive-rundown setup, since that
reader must remain protected and block recovery until released.

Each external-termination case starts a fresh scanner process, checks the
expected owned/retained objects, repeats recovery, and starts a fresh successful
transfer. Tests also keep external sentinel data and check it before and after
fixture teardown. These are process-crash tests on Windows, not simulated power
loss, an actual Windows reboot, or interactive Explorer acceptance.

Exact commit/run IDs, red/green results and quality-tool limitations are maintained
in [PR #12](https://github.com/landquart/Teleclav/pull/12). Crapper, Dryer and Mutator
do not support C#; no CRAP/duplication/mutation metrics are claimed.

## Microsoft contracts

- [CreateFile sharing and last-handle lifetime](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)
- [NtCreateFile relative names and reparse options](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntcreatefile)
- [Handle-based directory enumeration](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntquerydirectoryfile)
- [FILE_DISPOSITION_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_disposition_info)
- [TerminateProcess handle cleanup and I/O cancellation](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-terminateprocess)

## F3.5 read and cleanup verification

A published file is read only through the same handle used to verify it. The
guard opens its basename relative to the pinned operation directory with no
reparse traversal, then checks Published, the ownership EA, protected ACL,
recorded volume and file ID, file type and exactly one link. Read handles share
read access. Cleanup waits for cache reader leases and treats an incompatible
external handle as a bounded retry or a retained operation.

The Windows stress tests report total operation and cleanup time, process handle
counts, private and managed memory and remaining operation directories after 100
and 1,000 transfers. The samples during transfer are taken with the reader open.
Separate tests hold two readers of a 64 MiB file across eight concurrent
transfers and time recovery of a 256-file operation. See the
[F3.5 verification matrix](DRAG_DROP_SAFETY_AUDIT.md#f35-final-audit) for run
evidence and manual test gaps.

[Windows CI 78](https://github.com/landquart/Teleclav/actions/runs/41781366977/job/126413428450)
passed all 34 forced-crash test cases, including the fresh scanner after a
killed recovery process and a separate reader surviving its owner. Competing
recovery processes and live-owner refusal also passed in the staging suite.
A scanner's successful cleanup is checked against the expected directories
and external controls. The tests do not depend on PIDs for ownership. A
deterministically reused PID and an actual Windows reboot were not exercised.

The [F7 technical audit](TELECLAV_FINAL_TECHNICAL_AUDIT.md) tracks the current CI evidence and physical fault scenarios.
