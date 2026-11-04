# F5 - Ubuntu to Windows Explorer

Explorer receives descriptors for the selected files or the strict recursive
plan. The plan refuses links, special files, unsafe names and conflicting Windows
names. Both flat and nested selections reject case and Unicode normalization
collisions. A component can contain at most 255 UTF-16 code units, and a virtual
relative path must also fit FILEDESCRIPTORW. Explorer determines whether the
eventual destination path is usable.

For each requested file, the SFTP client checks the recorded size and UTC
modification time before opening. The recursive plan carries the timestamp from
the directory listing through to the Shell stream. Later checks compare visible
kind, size, timestamps and permission/ownership fields with the pre-open
observation. Checks of path components use parent READDIR metadata to observe
links. SSH.NET 2026.0.0 canonicalizes paths for Get/GetAttributes and OpenRead.
The public SftpFileStream.Length property uses FSTAT on the open handle before
and after the copy. SSH.NET exposes no public API for retrieving all FSTAT
attributes from that handle. The copy checks the exact number of bytes and
rejects premature EOF, extra data and observed cancellation.

The Windows Shell cache writes to the F3-owned staging stream. The guard records
ownership before writing and publishes only after the guarded download succeeds.
A failed or cancelled download does not produce a readable published cache
entry. F3 manages cleanup and recovery. Explorer owns the final destination.
Earlier files from a multi-item drop can remain if a subsequent file fails.

## Protocol boundaries

Standard SFTP v3 has no atomic, ancestor-pinned, no-follow open and provides no
stable file identity that links READDIR observations to OPEN. Concurrent
replacement or ancestor changes can evade checks by restoring names or matching
metadata. Modification times have server-defined granularity and can be set
back. Directory checks observe types around listing and opening. They cannot
establish a stable tree snapshot or detect entries omitted by a server without a
protocol error. Trusted host keys authenticate the endpoint. An authenticated
server can still fabricate file bytes, metadata or directory listings.
Independent hashes require an independently trusted source.

Normal transfers require standard SFTP only. The integration runner uses
Ubuntu 24.04 OpenSSH, forces internal-sftp and pins the host key. It runs the
download fixture with both key and password authentication. The local test
fixture changes a same-size file's mtime and replaces a parent with a symlink
after planning. The application connects with the same host-key verification
used elsewhere in Teleclav.

## Interactive acceptance

1. Extract the Windows x64 Portable ZIP for the exact PR head and start
   Heimdall.exe. Connect to a test Ubuntu OpenSSH account.
2. Use the fixture in [Drag and Drop acceptance](DRAG_DROP_ACCEPTANCE.md#reproducible-fixture-and-hashes).
   Drag a single file, a multi-file selection and a folder containing nested and
   empty directories, Unicode names, binary data and an empty file into separate
   empty folders in Explorer. Compare the directory trees and SHA-256 hashes.
3. Try a folder containing a symlink or two names that differ only by case.
   Confirm the refusal. During a separate large copy, cancel in Explorer, then
   repeat into an empty target. Confirm the successful retry's hash.
4. Disconnect SFTP during a large copy. Confirm the error, inspect Explorer's
   destination for partial results, and check staging cleanup after the
   transfer is released.

These Explorer checks require an interactive Windows desktop.
