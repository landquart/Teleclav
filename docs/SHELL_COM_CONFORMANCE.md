# Shell COM conformance F8

Base F7 head 38858b6d14b83914e0020aab30a68af157c88e0f passed CI 87.
The original F8 native fixture passed Debug and Release in CI 91. The
expanded conformance matrix below requires the subsequent F8 CI run.

## Implementation and ABI

The x64 C++ consumer uses Windows SDK declarations for IDataObject and
IStream through standard system proxies. The STA host pumps its dispatcher
while the MTA consumer calls interfaces in a separate process. A marshal
packet created with CoMarshalInterface is consumed by CoUnmarshalInterface.
Static assertions cover FORMATETC (32), STGMEDIUM (24), FILEDESCRIPTORW (592)
and STATSTG (80). QI covers IUnknown, IDataObject, ISequentialStream, IStream
and IDataObjectAsyncCapability. The async interface follows its documented
GUID and vtable order with BOOL and HRESULT marshaling.

GlobalAlloc/GlobalLock descriptor storage is released by ReleaseStgMedium.
Stream media carry IStream references released by ReleaseStgMedium. The
consumer also explicitly releases enumerators, clones, proxy references and
CoTaskMem-allocated target-device data.

IStream.Read fills intermediate short underlying reads before reporting EOF.
Clone starts at the original position and has an independent cursor. Clones
share a reference-counted source, a synchronization gate and the existing F3
lease. Only the last disposed or finalized clone closes the verified content.
Cloning never reopens a path or creates another staging file. Read, Seek and
Stat reject disposed objects. Seek uses checked 64-bit arithmetic.

## Expanded automated matrix

| Requirement | Test | Evidence |
| --- | --- | --- |
| All IDataObject methods | Native enumeration, query, GetData, unsupported methods and advisory HRESULTs | Native DataContract |
| Virtual directories and multiple roots | Unicode tree, three files and three directories including an empty directory | Native materialization and managed tree/SHA-256 assertions |
| Repeated and out-of-order content | Requests 5, 4, 1, 4 | Native recipient |
| Read, EOF, Seek, Stat, CopyTo and Clone | Native calls and independent SHA-256 of hello content | Native StreamContract |
| Read-only operations | Write and SetSize return STG_E_ACCESSDENIED | Native StreamContract |
| Revert and region locks | STG_E_INVALIDFUNCTION | Native StreamContract |
| Large offsets and sizes | Generated logical stream larger than 8 GiB; seeks across 4 GiB and EOF | Native LargeStream, no large disk allocation |
| Async, sync, cancellation, late EndOperation | Separate native recipient modes | ShellCrossProcessTests |
| Missing EndOperation and recipient termination | Separate noend and crash modes with bounded idle expiry | ShellCrossProcessTests |
| Concurrent clones and original disposal | Independent positions and retained content | ShellReadStreamTests |
| Failure propagation and lifetime regression | Existing stream, deferred, cache and async tests remain enabled | Full mandatory CI |

Local Clone red/green reproduced STG_E_INVALIDFUNCTION before implementation,
then verified independent positions and reading after original disposal.
The earlier short-read red/green reproduced one byte before and five after.

The native recipient uses BCrypt SHA-256 and the host uses .NET SHA-256.
The eight-GiB fixture is a generated logical stream. It checks counters and ABI,
not a physical multi-GiB SFTP transfer. The test host uses a short injected idle
deadline to exercise abandonment without waiting for the production deadline.

## Unsupported operations and remaining limits

Streams are read-only. Transaction rollback and region locks are unsupported.
IDataObject GetDataHere, SetData, canonicalization and DATADIR_SET enumeration
return E_NOTIMPL. Advisory methods return OLE_E_ADVISENOTSUPPORTED.
The native tests assert these HRESULTs. Ordinary Stream read/write failures
are still covered by the existing controlled managed fault tests.

Real Explorer and OLE DoDragDrop gestures remain NOT TESTED because the runner
has no interactive Windows 11 desktop. The native recipient demonstrates the
COM transport and ABI only. The OS can defer proxy rundown after a process dies.
F3 retains a file while any verified reader remains active.
Crapper, Dryer and Mutator are NOT RUN for C#.

## Official contracts

- https://learn.microsoft.com/en-us/windows/win32/api/objidl/nf-objidl-isequentialstream-read
- https://learn.microsoft.com/en-us/windows/win32/api/objidl/nf-objidl-istream-clone
- https://learn.microsoft.com/en-us/windows/win32/api/objidl/nf-objidl-istream-copyto
- https://learn.microsoft.com/en-us/windows/win32/api/combaseapi/nf-combaseapi-comarshalinterface
- https://learn.microsoft.com/en-us/windows/win32/api/combaseapi/nf-combaseapi-counmarshalinterface
