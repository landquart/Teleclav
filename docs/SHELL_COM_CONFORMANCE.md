# Shell COM conformance F8

Base F7 head 38858b6d14b83914e0020aab30a68af157c88e0f passed CI 87.
The original F8 native fixture passed Debug and Release in CI 91.
The expanded six-mode native matrix and Windows staging passed Debug in
[CI 96](https://github.com/landquart/Teleclav/actions/runs/41983949785) on
84ca5d48428d25d90f18295ea8d1aeb7f761674e. That run stopped on test import
ordering after the native and staging checks. The final PR head and complete
CI result are recorded in [PR 17](https://github.com/landquart/Teleclav/pull/17).
The final workflow repeats the matrix in Debug and the required Release suite.

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
The native tests assert these HRESULTs. GetDataHere uses a PreserveSig slot
to retain the caller-owned in/out medium on a failed return. Throwing through
the previous CLR stub reproduced RPC_X_BAD_STUB_DATA across the process boundary. Ordinary Stream read/write failures
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

## Review and execution

The production changes are limited to the stream cursors/shared lease,
ISequentialStream exposure and the IDataObject GetDataHere marshaling slot.
Existing source validation, the F3 journal format and handle-based publication
and cleanup remain the exercised transfer path. Native unsupported-method
checks reproduced the old GetDataHere failure as HRESULT 0x800706F7 before
the PreserveSig correction; the corrected slot returned E_NOTIMPL in CI 96.

For local Windows execution, build tests/native/ShellComConsumer.cpp with an
x64 MSVC developer shell and cl /std:c++17 /EHsc. Set
TELECLAV_NATIVE_COM_CONSUMER to the absolute executable path before running
ShellCrossProcessTests. CI performs this build and sets the variable itself.
