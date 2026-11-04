# Teleclav F7 technical acceptance

Release status: blocked by interactive Windows 11 Explorer acceptance.
F7 starts from F6 head a9c1312cb1230ba623f27d901922e2a094aebb6b. GitHub
reported PRs 2 through 15 open and unmerged at inspection. D5 includes the
reviewed infrastructure and incomplete-upload patches from PRs 2 and 3.
The stack from PR 8 through 15 carries integration, CI diagnostics, Shell,
COM lifetime, staging, local upload, remote download and performance work.

| Requirement | Check | Result | Evidence |
| --- | --- | --- | --- |
| Windows CI, Core, App, SFTP, SSH and Terminal | Required F6 Windows suite | PASS | CI 86, 19,414 passed |
| Shell and COM interop, IStream, cancellation, async lifetime | Automated Shell and COM regression | PASS | CI 86 Windows |
| Staging ACL, identity, ownership and journal | Windows staging suite | PASS | CI 86, 181 passed |
| Crash recovery and process coordination | Crash, active owner, competing recovery, blocked-file and cleanup tests | PASS | CI 86 Windows staging |
| Local source checks, links and partial uploads | Upload regression and no-clobber integration | PASS | CI 86 Windows and OpenSSH |
| Remote file validation, names and data integrity | Guarded SFTP and tree/hash tests | PASS | CI 86 Windows and Ubuntu |
| SSH key and password authentication | Ubuntu OpenSSH internal-sftp with pinned host key | PASS | CI 86, three tests |
| Portable publish and startup | Extracted F6 ZIP launch on runner | PASS | CI 86 Windows |
| F7 package, comparator and full regression | F7 workflow | NOT TESTED | Await F7 CI |
| Explorer in both directions, all destinations and error cases | Interactive Windows 11 | NOT TESTED | Owner acceptance required |
| Multi-GiB transfers and end-to-end performance | Isolated large-file matrix | NOT TESTED | F6 local planning and 64 MiB staging only |
| Physical disk exhaustion | Isolated NTFS volume | NOT TESTED | Dedicated volume required |
| Reboot or power loss | Physical fault test | NOT TESTED | Process-termination tests ran separately |

[CI 86](https://github.com/landquart/Teleclav/actions/runs/41921770981)

## Transfer boundaries and remaining risks

Upload starts with a local source plan. Windows checks opened source handles
against the plan and reads through the verified handles. SFTP stages content
before publication and the queue reports skipped entries. Standard SFTP
replacement retains a race after an explicit overwrite confirmation.

Download validates listed names and observable metadata before reading.
SFTP v3 leaves a race between path checks and opening. Matching metadata
can conceal a replacement and the server controls enumeration. The client
writes to the guarded staging stream, journals ownership and publishes
after successful transfer. COM retains the read lease while Explorer uses
it. Explorer writes the destination and may leave earlier items after a
later item fails. Recovery can retain objects with unknown identity or
active locks and runs with a bounded scan. See the [recovery design](STAGING_CRASH_RECOVERY.md).

## Owner acceptance

Download the F7 Portable artifact from its successful CI run. Extract the
outer artifact and compare the inner ZIP with the included SHA-256 file
using Get-FileHash. Extract the inner ZIP and run Heimdall.exe. The Standard
package includes the .NET runtime and requires WebView2 Evergreen on the
test PC. Use only disposable fixtures.

Follow the [fixture and verification guide](DRAG_DROP_ACCEPTANCE.md#reproducible-fixture-and-hashes).
Drag the fixture in both directions, verify each destination against the
manifest, and recheck the source after each copy. Repeat with Desktop,
Downloads, an arbitrary folder and another local disk as Windows targets.
Record Windows version, artifact commit, direction, scenario, destination,
result and comparator output. For cancellation or disconnection, record
completed and partial items and confirm source preservation before retry.
