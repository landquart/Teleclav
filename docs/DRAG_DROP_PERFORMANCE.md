# Drag and Drop performance investigation

Status: IN PROGRESS. This report records the existing evidence and pending F6
measurements. No improvement is claimed.

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

No F6 optimization or final regression result is recorded yet.
