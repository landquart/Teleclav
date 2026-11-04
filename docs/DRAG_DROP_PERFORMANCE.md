# Drag and Drop performance investigation

Status: IN PROGRESS. This report records the existing evidence and pending F6
measurements. The local benchmark covers metadata already in memory.

## Starting point

F5 head: 6f958406315b9a3ba6958fc62151a4071c2577d0. CI 84 passed on that commit.

The Windows Shell cache reserves the declared size of each requested file
against a 16 GiB operation-wide limit. Reserved entries remain cached for
repeat reads until the operation is cleaned up. A single file larger than the
limit or a selection whose requested entries exceed it cannot complete.

The SFTP browser serializes operations using its client lock. The bounded
download holds the lock while checking and reading the remote file. Impact on
latency and throughput must be measured under controlled network conditions.

## Historical F3 measurements

The F3 audit recorded 100 sequential staging operations in 7,325.24 ms,
1,000 in 119,848.53 ms, and recovery of a 256-file operation in 1,840.24 ms.
Both sequential tests left zero staging directories. These are CI 78 results
on an earlier commit, not F6 baseline measurements or before/after results.

See [the F3 audit](DRAG_DROP_SAFETY_AUDIT.md#regression-and-load-evidence).

## Measurements pending

On matching Windows environments, measure preparation, metadata, first byte,
throughput, UI latency, memory, handles, CPU, staging space, cancellation,
cleanup and recovery on the F5 base and the final F6 head. Include large files,
aggregate data above 16 GiB and the specified file-count matrix. Record server,
disk, network conditions and sample counts. Interactive Explorer scenarios
require a Windows desktop. Keep F3 ownership and recovery tests enabled.

## Local planning benchmark

Linux x64, .NET 10.0.103, Release, tiered compilation disabled. In-memory
listing with Unicode file names and one root directory. Each sample includes
the hierarchy plan and the FILEGROUPDESCRIPTORW bytes. Twenty warmups and 100
measured iterations per size. Allocations are managed bytes on the calling
thread. No network or Explorer was used.

| Files | F5 ms | F6 ms | F5 allocated bytes | F6 allocated bytes |
| ---: | ---: | ---: | ---: | ---: |
| 10 | 0.124 | 0.020 | 22,133 | 16,437 |
| 100 | 0.357 | 0.190 | 200,520 | 145,864 |
| 1,000 | 2.753 | 1.610 | 1,994,304 | 1,450,048 |
| 9,999 | 26.330 | 16.279 | 20,191,064 | 14,751,809 |

Elapsed times are from one process per version. The planner limits the
hierarchy to 10,000 entries including folders. The filename validator now
reuses a strict UTF-16 encoder and inspects the device-name stem as a span.

## Staging quota

Set TELECLAV_SHELL_CACHE_GIB before starting the application to select a
per-operation quota from 1 to 1024 GiB. Unset and invalid values use 16 GiB.
The cache still checks the actual free space before scheduling each new file.
It retains entries for repeat reads and releases them through existing F3
cleanup after the operation and its readers finish. A higher quota requires
sufficient disk space. Real transfers above 16 GiB remain unverified.

Windows regression and full end-to-end before/after measurements are pending.

The [F7 technical audit](TELECLAV_FINAL_TECHNICAL_AUDIT.md) links CI 86 and records the remaining throughput and Explorer checks.
