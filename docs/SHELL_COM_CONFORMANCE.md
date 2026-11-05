# Shell COM conformance F8

Status: verification in progress. Base F7 head
38858b6d14b83914e0020aab30a68af157c88e0f passed CI 87.

The native x64 consumer obtains IDataObject using CoUnmarshalInterface from a
packet produced by CoMarshalInterface in an STA host. The host pumps its
Dispatcher while the MTA consumer uses Windows COM proxies in another process.
The test queries IUnknown, enumerates formats, reads FILEGROUPDESCRIPTORW and
requests file streams in order 1, 0, 1. It checks Stat, Seek, full and short Read,
the exact hello payload and ReleaseStgMedium.

The CI builds the consumer with the Windows SDK headers and MSVC x64 tools.
The native calls use the SDK declarations for IDataObject and IStream, checking
the managed vtable through system proxies. Direct managed tests remain useful
for fault injection and lifetime cases.

A regression test requests five bytes from an underlying stream returning only
one byte per read. ISequentialStream S_FALSE denotes end of stream, so the
adapter must fill a request across intermediate short reads.

References:
- https://learn.microsoft.com/en-us/windows/win32/api/objidl/nf-objidl-isequentialstream-read
- https://learn.microsoft.com/en-us/windows/win32/api/combaseapi/nf-combaseapi-comarshalinterface
- https://learn.microsoft.com/en-us/windows/win32/api/combaseapi/nf-combaseapi-counmarshalinterface

## Pending acceptance

Native process execution, full Windows regression and red/green evidence await
CI. Explorer remains NOT TESTED. The initial native fixture covers two regular
files, not materialization of a complete hierarchy, consumer crashes or a full
OLE drag gesture. Clone remains unsupported and its compatibility needs further
review. Crapper, Dryer and Mutator are NOT RUN for C#.
