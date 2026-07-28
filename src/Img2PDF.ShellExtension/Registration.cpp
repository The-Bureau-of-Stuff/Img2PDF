#include "pch.h"
#include "Registration.h"
#include "Guid.h"

namespace
{
    std::filesystem::path GetThisModulePath()
    {
        HMODULE module = nullptr;
        GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&GetThisModulePath),
            &module);

        wchar_t path[MAX_PATH];
        DWORD length = GetModuleFileNameW(module, path, static_cast<DWORD>(std::size(path)));
        return std::filesystem::path(std::wstring(path, length));
    }

    LSTATUS SetStringValue(HKEY key, const wchar_t* valueName, const std::wstring& value)
    {
        DWORD sizeInBytes = static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t));
        return RegSetValueExW(key, valueName, 0, REG_SZ,
            reinterpret_cast<const BYTE*>(value.c_str()), sizeInBytes);
    }

    HRESULT RegisterClsid()
    {
        std::wstring clsidKeyPath = std::wstring(L"Software\\Classes\\CLSID\\") + CLSID_ExplorerCommandHandlerString;

        wil::unique_hkey clsidKey;
        LSTATUS status = RegCreateKeyExW(HKEY_CURRENT_USER, clsidKeyPath.c_str(), 0, nullptr,
            0, KEY_WRITE, nullptr, &clsidKey, nullptr);
        RETURN_IF_WIN32_ERROR(status);

        RETURN_IF_WIN32_ERROR(SetStringValue(clsidKey.get(), nullptr, L"Img2PDF Shell Extension"));

        wil::unique_hkey inprocKey;
        status = RegCreateKeyExW(clsidKey.get(), L"InprocServer32", 0, nullptr,
            0, KEY_WRITE, nullptr, &inprocKey, nullptr);
        RETURN_IF_WIN32_ERROR(status);

        RETURN_IF_WIN32_ERROR(SetStringValue(inprocKey.get(), nullptr, GetThisModulePath().wstring()));
        RETURN_IF_WIN32_ERROR(SetStringValue(inprocKey.get(), L"ThreadingModel", L"Apartment"));

        return S_OK;
    }

    // AllFilesystemObjects (not per-extension SystemFileAssociations) — a verb registered only
    // under e.g. SystemFileAssociations\.jpg\shell is pruned by Explorer's multi-selection
    // merge logic unless EVERY selected item's type also has it registered, which would make a
    // mixed supported/unsupported selection never even reach GetState. AllFilesystemObjects
    // covers every file and folder unconditionally, so GetState is the sole dynamic decider —
    // matching what §4.1 actually wants ("show as long as at least one item is usable").
    HRESULT RegisterVerb()
    {
        std::wstring keyPath = L"Software\\Classes\\AllFilesystemObjects\\shell\\Img2PDF";

        wil::unique_hkey verbKey;
        LSTATUS status = RegCreateKeyExW(HKEY_CURRENT_USER, keyPath.c_str(), 0, nullptr,
            0, KEY_WRITE, nullptr, &verbKey, nullptr);
        RETURN_IF_WIN32_ERROR(status);

        // Static fallback shown before GetTitle runs; GetTitle overrides with the live
        // 1-file/multi-file wording once Explorer actually invokes this handler.
        RETURN_IF_WIN32_ERROR(SetStringValue(verbKey.get(), nullptr, L"Combine to PDF"));
        RETURN_IF_WIN32_ERROR(SetStringValue(verbKey.get(), L"ExplorerCommandHandler", CLSID_ExplorerCommandHandlerString));

        return S_OK;
    }
}

HRESULT RegisterShellExtension()
{
    RETURN_IF_FAILED(RegisterClsid());
    RETURN_IF_FAILED(RegisterVerb());
    return S_OK;
}

HRESULT UnregisterShellExtension()
{
    RegDeleteTreeW(HKEY_CURRENT_USER, L"Software\\Classes\\AllFilesystemObjects\\shell\\Img2PDF");

    std::wstring clsidKeyPath = std::wstring(L"Software\\Classes\\CLSID\\") + CLSID_ExplorerCommandHandlerString;
    RegDeleteTreeW(HKEY_CURRENT_USER, clsidKeyPath.c_str());

    return S_OK;
}
