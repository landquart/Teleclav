// Copyright 2026. Licensed under the Apache License, Version 2.0.
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <bcrypt.h>
#include <fstream>
#include <vector>
#include <array>
#include <filesystem>
#include <cstring>
#include <string>
#include <stdexcept>
#include <iostream>
#include <thread>
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "uuid.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "bcrypt.lib")

struct __declspec(uuid("3D8B0590-F691-11D2-8EA9-006097DF5BD4")) AsyncCapability : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE SetAsyncMode(BOOL) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetAsyncMode(BOOL*) = 0;
    virtual HRESULT STDMETHODCALLTYPE StartOperation(IBindCtx*) = 0;
    virtual HRESULT STDMETHODCALLTYPE InOperation(BOOL*) = 0;
    virtual HRESULT STDMETHODCALLTYPE EndOperation(HRESULT, IBindCtx*, DWORD) = 0;
};
static_assert(sizeof(FORMATETC) == 32);
static_assert(sizeof(STGMEDIUM) == 24);
static_assert(sizeof(FILEDESCRIPTORW) == 592);
static_assert(sizeof(STATSTG) == 80);
static void CheckAt(bool ok, int line) {
    if (!ok) throw std::runtime_error("COM contract failed at line " + std::to_string(line));
}
#define Check(value) CheckAt((value), __LINE__)

class AdviseSink final : public IAdviseSink {
    LONG references = 1;
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (iid != IID_IUnknown && iid != IID_IAdviseSink) return E_NOINTERFACE;
        *result = static_cast<IAdviseSink*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&references); }
    ULONG STDMETHODCALLTYPE Release() override {
        ULONG count = InterlockedDecrement(&references);
        if (!count) delete this;
        return count;
    }
    void STDMETHODCALLTYPE OnDataChange(FORMATETC*, STGMEDIUM*) override { }
    void STDMETHODCALLTYPE OnViewChange(DWORD, LONG) override { }
    void STDMETHODCALLTYPE OnRename(IMoniker*) override { }
    void STDMETHODCALLTYPE OnSave() override { }
    void STDMETHODCALLTYPE OnClose() override { }
};

static FORMATETC Format(const wchar_t* name, LONG index, DWORD medium) {
    return {static_cast<CLIPFORMAT>(RegisterClipboardFormatW(name)), nullptr, DVASPECT_CONTENT, index, medium};
}
static void VerifyHash(const char* bytes, ULONG size) {
    const std::array<BYTE, 32> expected{
        0x2c,0xf2,0x4d,0xba,0x5f,0xb0,0xa3,0x0e,0x26,0xe8,0x3b,0x2a,0xc5,0xb9,0xe2,0x9e,
        0x1b,0x16,0x1e,0x5c,0x1f,0xa7,0x42,0x5e,0x73,0x04,0x33,0x62,0x93,0x8b,0x98,0x24};
    std::array<BYTE, 32> actual{};
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    Check(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) == 0);
    NTSTATUS status = BCryptHash(algorithm, nullptr, 0,
        reinterpret_cast<PUCHAR>(const_cast<char*>(bytes)), size, actual.data(), static_cast<ULONG>(actual.size()));
    BCryptCloseAlgorithmProvider(algorithm, 0);
    Check(status == 0 && actual == expected);
}
static void StreamContract(IStream* stream) {
    ISequentialStream* sequential = nullptr;
    Check(stream->QueryInterface(IID_ISequentialStream, reinterpret_cast<void**>(&sequential)) == S_OK);
    sequential->Release();
    STATSTG stat{};
    Check(stream->Stat(&stat, STATFLAG_NONAME) == S_OK && stat.cbSize.QuadPart == 5);
    ULONG read = 9;
    char bytes[8]{};
    Check(stream->Read(bytes, 0, &read) == S_OK && read == 0);
    Check(stream->Read(bytes, 8, &read) == S_FALSE && read == 5);
    VerifyHash(bytes, read);
    LARGE_INTEGER offset{};
    offset.QuadPart = -2;
    Check(stream->Seek(offset, STREAM_SEEK_END, nullptr) == S_OK);
    Check(stream->Read(bytes, 2, &read) == S_OK && read == 2 && bytes[0] == 'l' && bytes[1] == 'o');
    offset.QuadPart = -1;
    Check(stream->Seek(offset, STREAM_SEEK_CUR, nullptr) == S_OK);
    Check(stream->Read(bytes, 1, &read) == S_OK && read == 1 && bytes[0] == 'o');
    offset.QuadPart = 0;
    Check(stream->Seek(offset, STREAM_SEEK_SET, nullptr) == S_OK);
    IStream* clone = nullptr;
    Check(stream->Clone(&clone) == S_OK && clone != nullptr);
    Check(clone->Read(bytes, 1, &read) == S_OK && bytes[0] == 'h');
    Check(stream->Read(bytes, 1, &read) == S_OK && bytes[0] == 'h');
    clone->Release();
    Check(stream->Commit(STGC_DEFAULT) == S_OK);
    ULONG written = 0;
    Check(stream->Write(bytes, 1, &written) == STG_E_ACCESSDENIED);
    ULARGE_INTEGER size{};
    Check(stream->SetSize(size) == STG_E_ACCESSDENIED);
    Check(stream->Revert() == STG_E_INVALIDFUNCTION);
    Check(stream->LockRegion(size, size, LOCK_EXCLUSIVE) == STG_E_INVALIDFUNCTION);
    Check(stream->UnlockRegion(size, size, LOCK_EXCLUSIVE) == STG_E_INVALIDFUNCTION);
    offset.QuadPart = 0;
    Check(stream->Seek(offset, STREAM_SEEK_SET, nullptr) == S_OK);
    IStream* target = nullptr;
    Check(CreateStreamOnHGlobal(nullptr, TRUE, &target) == S_OK);
    size.QuadPart = 8;
    ULARGE_INTEGER copied{}, accepted{};
    Check(stream->CopyTo(target, size, &copied, &accepted) == S_OK);
    Check(copied.QuadPart == 5 && accepted.QuadPart == 5);
    Check(target->Seek(offset, STREAM_SEEK_SET, nullptr) == S_OK);
    Check(target->Read(bytes, 5, &read) == S_OK && read == 5);
    VerifyHash(bytes, read);
    target->Release();
}
static void DataContract(IDataObject* data) {
    IUnknown* unknown = nullptr;
    Check(data->QueryInterface(IID_IUnknown, reinterpret_cast<void**>(&unknown)) == S_OK);
    unknown->Release();
    IEnumFORMATETC* formats = nullptr;
    Check(data->EnumFormatEtc(DATADIR_GET, &formats) == S_OK);
    FORMATETC offered{};
    ULONG fetched = 0;
    unsigned count = 0;
    while (formats->Next(1, &offered, &fetched) == S_OK) {
        Check(data->QueryGetData(&offered) == S_OK);
        CoTaskMemFree(offered.ptd);
        ++count;
    }
    formats->Release();
    Check(count == 6); // descriptor, three files, preferred effect and internal marker
    Check(data->EnumFormatEtc(DATADIR_SET, &formats) == E_NOTIMPL);
    FORMATETC format = Format(CFSTR_FILEDESCRIPTORW, -1, TYMED_HGLOBAL), canonical{};
    Check(data->GetCanonicalFormatEtc(&format, &canonical) == E_NOTIMPL);
    STGMEDIUM here{};
    here.tymed = TYMED_HGLOBAL;
    here.hGlobal = GlobalAlloc(GHND, 4096);
    Check(here.hGlobal != nullptr);
    HRESULT hereResult = data->GetDataHere(&format, &here);
    std::cout << "GetDataHere HGLOBAL HRESULT=" << std::hex << hereResult << std::dec << std::endl;
    Check(hereResult == E_NOTIMPL);
    FORMATETC streamFormat = Format(CFSTR_FILECONTENTS, 1, TYMED_ISTREAM);
    STGMEDIUM streamMedium{};
    streamMedium.tymed = TYMED_ISTREAM;
    Check(CreateStreamOnHGlobal(nullptr, TRUE, &streamMedium.pstm) == S_OK);
    HRESULT streamHereResult = data->GetDataHere(&streamFormat, &streamMedium);
    std::cout << "GetDataHere IStream HRESULT=" << std::hex << streamHereResult << std::dec << std::endl;
    Check(streamHereResult == E_NOTIMPL);
    ReleaseStgMedium(&streamMedium);
    Check(data->SetData(&format, &here, FALSE) == E_NOTIMPL);
    ReleaseStgMedium(&here);
    DWORD connection = 0;
    auto sink = new AdviseSink();
    HRESULT adviseResult = data->DAdvise(&format, ADVF_NODATA, sink, &connection);
    sink->Release();
    Check(adviseResult == OLE_E_ADVISENOTSUPPORTED);
    Check(data->DUnadvise(0) == OLE_E_ADVISENOTSUPPORTED);
    IEnumSTATDATA* advice = nullptr;
    Check(data->EnumDAdvise(&advice) == OLE_E_ADVISENOTSUPPORTED);
    format = Format(CFSTR_FILECONTENTS, 0, TYMED_ISTREAM);
    Check(data->QueryGetData(&format) == DV_E_FORMATETC); // directory
    STGMEDIUM missing{};
    Check(data->GetData(&format, &missing) == DV_E_FORMATETC);
    format.lindex = 99;
    Check(data->QueryGetData(&format) == DV_E_FORMATETC);
    format = Format(L"Preferred DropEffect", -1, TYMED_HGLOBAL);
    Check(data->GetData(&format, &here) == S_OK);
    auto effect = static_cast<DWORD*>(GlobalLock(here.hGlobal));
    Check(effect != nullptr && *effect == DROPEFFECT_COPY);
    GlobalUnlock(here.hGlobal);
    ReleaseStgMedium(&here);
}
static void LargeStream(IStream* stream) {
    STATSTG stat{};
    Check(stream->Stat(&stat, STATFLAG_NONAME) == S_OK && stat.cbSize.QuadPart == 0x200000005ULL);
    LARGE_INTEGER move{};
    move.QuadPart = 0x100000003LL;
    ULARGE_INTEGER position{};
    Check(stream->Seek(move, STREAM_SEEK_SET, &position) == S_OK && position.QuadPart == 0x100000003ULL);
    char byte = 0; ULONG read = 0;
    Check(stream->Read(&byte, 1, &read) == S_OK && read == 1 && byte == 3);
    move.QuadPart = -1;
    Check(stream->Seek(move, STREAM_SEEK_END, &position) == S_OK && position.QuadPart == 0x200000004ULL);
    Check(stream->Read(&byte, 1, &read) == S_OK && read == 1 && byte == 4);
    Check(stream->Read(&byte, 1, &read) == S_FALSE && read == 0);
    move.QuadPart = 4;
    Check(stream->Seek(move, STREAM_SEEK_END, &position) == S_OK);
    Check(stream->Read(&byte, 1, &read) == S_FALSE && read == 0);
    move.QuadPart = -1;
    Check(stream->Seek(move, STREAM_SEEK_SET, nullptr) == STG_E_SEEKERROR);
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 4) return 2;
    if (FAILED(CoInitializeEx(nullptr, COINIT_MULTITHREADED))) return 3;
    try {
        std::wstring mode = argv[2];
        std::ifstream file(argv[1], std::ios::binary);
        std::vector<char> bytes((std::istreambuf_iterator<char>(file)), {});
        IStream* packet = SHCreateMemStream(reinterpret_cast<const BYTE*>(bytes.data()), static_cast<UINT>(bytes.size()));
        Check(packet != nullptr);
        if (mode == L"large") {
            IStream* stream = nullptr;
            Check(CoUnmarshalInterface(packet, IID_IStream, reinterpret_cast<void**>(&stream)) == S_OK);
            packet->Release();
            LargeStream(stream);
            stream->Release();
        } else {
            IDataObject* data = nullptr;
            Check(CoUnmarshalInterface(packet, IID_IDataObject, reinterpret_cast<void**>(&data)) == S_OK);
            packet->Release();
            DataContract(data);
            AsyncCapability* async = nullptr;
            Check(data->QueryInterface(__uuidof(AsyncCapability), reinterpret_cast<void**>(&async)) == S_OK);
            BOOL active = FALSE;
            Check(async->SetAsyncMode(mode == L"sync" ? FALSE : TRUE) == S_OK);
            Check(async->GetAsyncMode(&active) == S_OK && active == (mode != L"sync"));
            if (mode != L"sync") {
                Check(async->StartOperation(nullptr) == S_OK);
                Check(FAILED(async->StartOperation(nullptr)));
                Check(async->InOperation(&active) == S_OK && active);
            }
            FORMATETC descriptor = Format(CFSTR_FILEDESCRIPTORW, -1, TYMED_HGLOBAL);
            STGMEDIUM medium{};
            Check(data->GetData(&descriptor, &medium) == S_OK);
            auto group = static_cast<FILEGROUPDESCRIPTORW*>(GlobalLock(medium.hGlobal));
            Check(group != nullptr && group->cItems == 6);
            std::vector<FILEDESCRIPTORW> entries(group->fgd, group->fgd + group->cItems);
            GlobalUnlock(medium.hGlobal);
            ReleaseStgMedium(&medium);
            std::filesystem::path output(argv[3]);
            for (const auto& entry : entries)
                if ((entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
                    std::filesystem::create_directories(output / entry.cFileName);
            if (mode == L"pendingcancel") {
                FORMATETC content = Format(CFSTR_FILECONTENTS, 1, TYMED_ISTREAM);
                Check(data->GetData(&content, &medium) == S_OK);
                auto request = std::filesystem::path(std::wstring(argv[1]) + L".cancel");
                std::thread cancel([request] { Sleep(200); std::ofstream(request).close(); });
                char bytes[5]{}; ULONG read = 0;
                HRESULT result = medium.pstm->Read(bytes, 5, &read);
                cancel.join();
                Check(FAILED(result));
                ReleaseStgMedium(&medium);
                Check(FAILED(data->GetData(&content, &medium)));
            }
            else for (LONG index : {5, 4, 1, 4}) {
                FORMATETC content = Format(CFSTR_FILECONTENTS, index, TYMED_ISTREAM);
                Check(data->GetData(&content, &medium) == S_OK);
                if (mode == L"crash") { ExitProcess(0); } // intentionally abandon live proxies
                StreamContract(medium.pstm);
                LARGE_INTEGER start{};
                Check(medium.pstm->Seek(start, STREAM_SEEK_SET, nullptr) == S_OK);
                char contentBytes[5]{}; ULONG read = 0;
                Check(medium.pstm->Read(contentBytes, 5, &read) == S_OK && read == 5);
                std::ofstream destination(output / entries[index].cFileName, std::ios::binary);
                destination.write(contentBytes, 5);
                destination.close();
                Check(destination.good());
                IStream* retained = nullptr;
                Check(medium.pstm->Clone(&retained) == S_OK);
                ReleaseStgMedium(&medium);
                Check(retained->Seek(start, STREAM_SEEK_SET, nullptr) == S_OK);
                Check(retained->Read(contentBytes, 5, &read) == S_OK && read == 5);
                retained->Release();
            }
            if (mode == L"cancel") {
                auto request = std::filesystem::path(std::wstring(argv[1]) + L".cancel");
                std::ofstream(request).close();
                auto signal = std::filesystem::path(std::wstring(argv[1]) + L".cancelled");
                for (int i = 0; i < 1000 && !std::filesystem::exists(signal); ++i) Sleep(10);
                Check(std::filesystem::exists(signal));
                FORMATETC content = Format(CFSTR_FILECONTENTS, 1, TYMED_ISTREAM);
                Check(FAILED(data->GetData(&content, &medium)));
            }
            if (mode != L"sync" && mode != L"noend") {
                Check(async->EndOperation(S_OK, nullptr, DROPEFFECT_COPY) == S_OK);
                Check(async->InOperation(&active) == S_OK && !active);
                Check(async->EndOperation(S_OK, nullptr, DROPEFFECT_COPY) == S_OK);
            }
            async->Release();
            data->Release();
        }
        CoUninitialize();
        std::cout << "PASS native cross-process contract" << std::endl;
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << std::endl;
        CoUninitialize();
        return 1;
    }
}
