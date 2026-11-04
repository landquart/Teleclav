# Windows to Ubuntu upload safety (F4)

## Source selection and reading

The drop and upload commands share the existing transfer queue and tree planner.
Windows plans record the volume and file ID of every component. Directory
enumeration and child capture use the verified directory handle. Each relative
open is one component with reparse traversal disabled. Before sending a file,
the browser reopens the chain, compares it with the plan, and reads from the
verified handle. The reader shares read access only. It stays open through
publication. The staged copy requires exactly the planned length, and Windows
checks the source ID, attributes, link count, length and modification time again
before publishing.

The Windows source contract covers ordinary files and folders on local NTFS,
including NTFS removable drives exposed with a drive letter. UNC paths, other
filesystems, alternate data streams, reparse points and multi-link files are
refused. The product keeps the existing non-Windows path for transport tests and
other portable callers. The NTFS guarantee depends on Windows file sharing,
the integrity of the OS and the drive mapping. A privileged actor, a hostile
same-user process with out-of-band write access, or a file modified with
identical observable metadata before the read handle opens is outside this
guarantee. The plan is captured when the queued job starts.

Native references include [NtCreateFile](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntcreatefile),
[NtQueryDirectoryFile](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntquerydirectoryfile),
[CreateFileW sharing](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)
and [FILE_ID_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info).

## Publication and failures

Each file is sent to an exclusively created random sibling name. Long target
names use a short temporary prefix to stay within the server component limit. The
temporary mode is tightened to 0600 and verified before the first byte. Size or
source validation failures and cancellation prevent publication and trigger
best-effort cleanup of the reserved temporary path. New files use the SFTP v3
rename without overwrite permission. The Ubuntu CI account forces internal-sftp
to verify that creation, cancellation and retry work without a shell. An
explicit replacement retains the existing metadata-preservation policy, which
may refuse replacement if the server provides no trusted exec channel for
metadata checks. A target absent at the replacement preflight uses exclusive
creation. An existing target's kind and observable attributes are checked again
before the replacing rename.

SFTP v3 has no compare-and-swap operation for a remote file ID. The stat checks
do not close the final rename race or detect a replacement with identical
attributes. The remote target directory and its ancestors must be trusted and
stable. SSH.NET canonicalizes rename arguments, so the client also refuses an existing
name through a parent listing immediately before a no-clobber rename.
No-clobber depends on a conforming server and the server filesystem.
A transport failure after commit can leave the final outcome uncertain, and
rollback can leave the temporary path after disconnect. Refresh the destination
before manually retrying. Retry rebuilds the conflict inventory and asks again
when there are conflicts. The client never retries publication automatically.

The queue reports a batch with refused or skipped entries as Failed, with the
skip details and available progress. The status is emitted after the directory
refresh, after the batch verdict is known. Previously committed entries are
retained on a later failure or cancellation. The Windows queued upload does not
automatically reopen a verified source through the privileged fallback after
a remote permission error.

## Interactive acceptance

1. Start the Portable artifact for the exact PR head on Windows 11. Connect to
   a test Ubuntu OpenSSH account and select an empty target.
2. Drag a file and a mixed selection with nested and empty folders, Unicode
   names and a long filename from Explorer. Compare tree, sizes and SHA-256
   on both ends with the fixture and comparator in
   [Drag & Drop acceptance](DRAG_DROP_ACCEPTANCE.md#reproducible-fixture-and-hashes).
3. Repeat the drop. Exercise Skip and Rename, then explicit Replace where the
   server can verify the destination metadata. Check the queue, final status
   and preserved content after each decision.
4. Cancel a large upload after progress begins, then reconnect or refresh and
   retry. A completed file must have the expected hash. Check that no partial
   file was published under the final name. If disconnected, inspect the remote
   directory for a leftover temporary file.
5. Exercise a linked source, an ordinary file on an external NTFS drive, and a
   UNC source. Confirm that refused sources have no corresponding upload and
   that mixed batches do not report full completion.
