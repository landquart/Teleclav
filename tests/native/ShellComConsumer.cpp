// Copyright 2026. Licensed under the Apache License, Version 2.0.
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <fstream>
#include <vector>
#include <cstring>
#include <stdexcept>
#include <iostream>
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "uuid.lib")
#pragma comment(lib, "user32.lib")

struct __declspec(uuid("3D8B0590-F691-11D2-8EA9-006097DF5BD4")) AsyncCapability : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE SetAsyncMode(BOOL) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetAsyncMode(BOOL*) = 0;
    virtual HRESULT STDMETHODCALLTYPE StartOperation(IBindCtx*) = 0;
    virtual HRESULT STDMETHODCALLTYPE InOperation(BOOL*) = 0;
    virtual HRESULT STDMETHODCALLTYPE EndOperation(HRESULT, IBindCtx*, DWORD) = 0;
};
static void Check(bool ok) { if (!ok) throw std::runtime_error("COM contract failed"); }
int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 2;
    HRESULT initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(initialized)) return 3;
    try {
        std::ifstream file(argv[1], std::ios::binary);
        std::vector<char> bytes((std::istreambuf_iterator<char>(file)), {});
        IStream* packet = SHCreateMemStream(reinterpret_cast<const BYTE*>(bytes.data()), static_cast<UINT>(bytes.size()));
        Check(packet != nullptr);
        IDataObject* data = nullptr;
        HRESULT hr = CoUnmarshalInterface(packet, IID_IDataObject, reinterpret_cast<void**>(&data));
        packet->Release();
        Check(SUCCEEDED(hr));
        IUnknown* unknown = nullptr;
        Check(SUCCEEDED(data->QueryInterface(IID_IUnknown, reinterpret_cast<void**>(&unknown))));
        unknown->Release();
        AsyncCapability* async = nullptr;
        Check(SUCCEEDED(data->QueryInterface(__uuidof(AsyncCapability), reinterpret_cast<void**>(&async))));
        Check(async->SetAsyncMode(TRUE) == S_OK);
        BOOL active = FALSE;
        Check(async->GetAsyncMode(&active) == S_OK && active);
        Check(async->StartOperation(nullptr) == S_OK);
        Check(FAILED(async->StartOperation(nullptr)));
        Check(async->InOperation(&active) == S_OK && active);
        IEnumFORMATETC* formats = nullptr;
        Check(SUCCEEDED(data->EnumFormatEtc(DATADIR_GET, &formats)));
        FORMATETC offered{};
        ULONG fetched = 0;
        unsigned count = 0;
        while (formats->Next(1, &offered, &fetched) == S_OK) {
            Check(data->QueryGetData(&offered) == S_OK);
            CoTaskMemFree(offered.ptd);
            ++count;
        }
        formats->Release();
        Check(count >= 4);
        FORMATETC descriptor{static_cast<CLIPFORMAT>(RegisterClipboardFormatW(CFSTR_FILEDESCRIPTORW)), nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
        STGMEDIUM medium{};
        Check(SUCCEEDED(data->GetData(&descriptor, &medium)));
        auto group = static_cast<FILEGROUPDESCRIPTORW*>(GlobalLock(medium.hGlobal));
        Check(group != nullptr && group->cItems == 2);
        Check(group->fgd[0].nFileSizeLow == 5 && group->fgd[1].nFileSizeLow == 5);
        GlobalUnlock(medium.hGlobal);
        ReleaseStgMedium(&medium);
        for (LONG index : {1, 0, 1}) {
            FORMATETC content{static_cast<CLIPFORMAT>(RegisterClipboardFormatW(CFSTR_FILECONTENTS)), nullptr, DVASPECT_CONTENT, index, TYMED_ISTREAM};
            Check(SUCCEEDED(data->GetData(&content, &medium)));
            STATSTG stat{};
            Check(SUCCEEDED(medium.pstm->Stat(&stat, STATFLAG_NONAME)) && stat.cbSize.QuadPart == 5);
            char buffer[8]{};
            ULONG read = 0;
            Check(medium.pstm->Read(buffer, 8, &read) == S_FALSE && read == 5 && std::memcmp(buffer, "hello", 5) == 0);
            LARGE_INTEGER start{};
            Check(SUCCEEDED(medium.pstm->Seek(start, STREAM_SEEK_SET, nullptr)));
            Check(medium.pstm->Read(buffer, 5, &read) == S_OK && read == 5);
            Check(medium.pstm->Read(buffer, 1, &read) == S_FALSE && read == 0);
            ReleaseStgMedium(&medium);
        }
        Check(async->EndOperation(S_OK, nullptr, DROPEFFECT_COPY) == S_OK);
        Check(async->InOperation(&active) == S_OK && !active);
        async->Release();
        data->Release();
        CoUninitialize();
        std::cout << "PASS native out-of-process IDataObject and IStream" << std::endl;
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << std::endl;
        CoUninitialize();
        return 1;
    }
}
