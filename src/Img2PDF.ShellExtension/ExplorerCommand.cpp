#include "pch.h"
#include "ExplorerCommand.h"
#include "SupportedExtensions.h"

#include <objbase.h>
#include <shlobj.h>

using Microsoft::WRL::ComPtr;

namespace
{
    // This DLL's own path — used both to find the sibling app exe and, during registration, to
    // point InprocServer32 at the right file. Explorer loads this DLL in-proc, so
    // GetModuleHandleExW against a static address inside this module always resolves to it.
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

    // Same install directory as this DLL — the eventual MSIX layout ships the app exe and this
    // DLL side by side, so this is the real long-term layout, not a dev-only stand-in.
    std::filesystem::path GetAppExePath()
    {
        return GetThisModulePath().parent_path() / L"Img2PDF.App.exe";
    }

    std::vector<std::wstring> GetSelectedPaths(IShellItemArray* items)
    {
        std::vector<std::wstring> paths;

        DWORD count = 0;
        THROW_IF_FAILED(items->GetCount(&count));
        paths.reserve(count);

        for (DWORD i = 0; i < count; ++i)
        {
            ComPtr<IShellItem> item;
            THROW_IF_FAILED(items->GetItemAt(i, &item));

            wil::unique_cotaskmem_string path;
            THROW_IF_FAILED(item->GetDisplayName(SIGDN_FILESYSPATH, &path));
            paths.emplace_back(path.get());
        }

        return paths;
    }

    bool IsSupportedFile(const std::filesystem::path& path)
    {
        std::error_code ec;
        if (path.empty() || std::filesystem::is_directory(path, ec))
        {
            return false;
        }

        return Img2PDF::ShellExtension::IsSupportedExtension(path.extension().wstring());
    }

    struct FolderContents
    {
        std::vector<std::wstring> supportedFiles;
        std::vector<std::wstring> skippedFileNames;
    };

    // Non-recursive — matches MainViewModel.LoadFolderAsync's existing folder-launch behaviour,
    // so a directly-selected folder behaves the same whether opened via the shell extension or
    // the app's own folder-path launch path. Unsupported files directly inside are reported as
    // skipped, same as an unsupported file picked directly in the selection — nested
    // subfolders are simply not part of this non-recursive listing at all, not "skipped".
    FolderContents ExpandFolder(const std::filesystem::path& folder)
    {
        FolderContents contents;
        std::error_code ec;
        for (const std::filesystem::directory_entry& entry : std::filesystem::directory_iterator(folder, ec))
        {
            if (entry.is_directory(ec))
            {
                continue;
            }

            if (IsSupportedFile(entry.path()))
            {
                contents.supportedFiles.push_back(entry.path().wstring());
            }
            else
            {
                contents.skippedFileNames.push_back(entry.path().filename().wstring());
            }
        }

        return contents;
    }

    // A directory only counts as "usable" if it directly contains at least one supported file —
    // an empty folder, or one full of unrelated files, is still skipped like any other unusable
    // item (spec §10: no unprompted recursive scanning either, so this stays one level deep).
    bool IsSupportedFolder(const std::filesystem::path& path)
    {
        std::error_code ec;
        if (!std::filesystem::is_directory(path, ec))
        {
            return false;
        }

        for (const std::filesystem::directory_entry& entry : std::filesystem::directory_iterator(path, ec))
        {
            if (IsSupportedFile(entry.path()))
            {
                return true;
            }
        }

        return false;
    }

    bool IsUsableSelection(const std::wstring& path)
    {
        std::filesystem::path p(path);
        return IsSupportedFile(p) || IsSupportedFolder(p);
    }

    std::filesystem::path GetTempDir()
    {
        wil::unique_cotaskmem_string localAppData;
        THROW_IF_FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &localAppData));

        std::filesystem::path dir = std::filesystem::path(localAppData.get()) / L"Temp" / L"Img2PDF";
        std::filesystem::create_directories(dir);
        return dir;
    }

    std::wstring NewGuidText()
    {
        GUID guid;
        THROW_IF_FAILED(CoCreateGuid(&guid));
        wil::unique_cotaskmem_string guidString;
        THROW_IF_FAILED(StringFromCLSID(guid, &guidString));

        // StringFromCLSID formats as "{...}" — strip the braces for a plain filename.
        std::wstring text = guidString.get();
        if (text.size() >= 2)
        {
            text = text.substr(1, text.size() - 2);
        }

        return text;
    }

    // %LOCALAPPDATA%\Temp\Img2PDF\{guid}.<extension> — per spec §4.1, paths are never passed on
    // the command line (Explorer selections routinely blow past the ~32k char limit). Used for
    // both the supported-files list and the skipped-files list.
    std::filesystem::path WriteLinesFile(const std::vector<std::wstring>& lines, const wchar_t* extension)
    {
        std::filesystem::path file = GetTempDir() / (NewGuidText() + extension);

        std::ofstream stream(file, std::ios::binary | std::ios::trunc);
        THROW_HR_IF(E_UNEXPECTED, !stream.is_open());

        for (const std::wstring& line : lines)
        {
            int utf8Length = WideCharToMultiByte(CP_UTF8, 0, line.c_str(), -1, nullptr, 0, nullptr, nullptr);
            THROW_HR_IF(E_UNEXPECTED, utf8Length <= 0);

            std::string utf8(static_cast<size_t>(utf8Length) - 1, '\0');
            WideCharToMultiByte(CP_UTF8, 0, line.c_str(), -1, utf8.data(), utf8Length, nullptr, nullptr);

            stream << utf8 << '\n';
        }

        return file;
    }
}

IFACEMETHODIMP ExplorerCommandHandler::GetTitle(IShellItemArray* items, PWSTR* name)
{
    DWORD count = 0;
    if (items)
    {
        items->GetCount(&count);
    }

    const wchar_t* title = (count == 1) ? L"Convert to PDF" : L"Combine to PDF";
    return SHStrDupW(title, name);
}

IFACEMETHODIMP ExplorerCommandHandler::GetIcon(IShellItemArray* /*items*/, PWSTR* icon)
{
    try
    {
        return SHStrDupW(GetAppExePath().c_str(), icon);
    }
    catch (...)
    {
        *icon = nullptr;
        return E_FAIL;
    }
}

IFACEMETHODIMP ExplorerCommandHandler::GetToolTip(IShellItemArray* /*items*/, PWSTR* infoTip)
{
    *infoTip = nullptr;
    return E_NOTIMPL;
}

IFACEMETHODIMP ExplorerCommandHandler::GetCanonicalName(GUID* guidCommandName)
{
    *guidCommandName = GUID_NULL;
    return S_OK;
}

IFACEMETHODIMP ExplorerCommandHandler::GetState(IShellItemArray* items, BOOL /*okToBeSlow*/, EXPCMDSTATE* cmdState)
{
    // Never throw out of a shell extension (spec §10) — any failure here degrades to
    // "hidden", not a crashed Explorer.
    try
    {
        if (!items)
        {
            *cmdState = ECS_HIDDEN;
            return S_OK;
        }

        std::vector<std::wstring> paths = GetSelectedPaths(items);

        // Show as soon as at least one selected item is usable — the rest are silently skipped
        // and reported by the app on launch, rather than hiding the whole command for one bad file.
        bool anySupported = std::any_of(paths.begin(), paths.end(), IsUsableSelection);
        *cmdState = anySupported ? ECS_ENABLED : ECS_HIDDEN;
        return S_OK;
    }
    catch (...)
    {
        *cmdState = ECS_HIDDEN;
        return S_OK;
    }
}

IFACEMETHODIMP ExplorerCommandHandler::GetFlags(EXPCMDFLAGS* flags)
{
    *flags = ECF_DEFAULT;
    return S_OK;
}

IFACEMETHODIMP ExplorerCommandHandler::EnumSubCommands(IEnumExplorerCommand** enumCommands)
{
    *enumCommands = nullptr;
    return E_NOTIMPL;
}

IFACEMETHODIMP ExplorerCommandHandler::Invoke(IShellItemArray* items, IBindCtx* /*bindCtx*/)
{
    try
    {
        if (!items)
        {
            return S_OK;
        }

        std::vector<std::wstring> paths = GetSelectedPaths(items);

        std::vector<std::wstring> supported;
        std::vector<std::wstring> skippedNames;
        for (const std::wstring& pathText : paths)
        {
            std::filesystem::path path(pathText);
            if (IsSupportedFile(path))
            {
                supported.push_back(pathText);
            }
            else if (IsSupportedFolder(path))
            {
                FolderContents contents = ExpandFolder(path);
                supported.insert(supported.end(), contents.supportedFiles.begin(), contents.supportedFiles.end());
                skippedNames.insert(skippedNames.end(), contents.skippedFileNames.begin(), contents.skippedFileNames.end());
            }
            else
            {
                skippedNames.push_back(path.filename().wstring());
            }
        }

        if (supported.empty())
        {
            return S_OK;
        }

        std::filesystem::path listFile = WriteLinesFile(supported, L".list");
        std::filesystem::path appExe = GetAppExePath();

        std::wstring args = L"--list \"" + listFile.wstring() + L"\"";

        if (!skippedNames.empty())
        {
            std::filesystem::path skippedFile = WriteLinesFile(skippedNames, L".skipped");
            args += L" --skipped \"" + skippedFile.wstring() + L"\"";
        }

        HINSTANCE result = ShellExecuteW(nullptr, L"open", appExe.c_str(), args.c_str(), nullptr, SW_SHOW);
        if (reinterpret_cast<INT_PTR>(result) <= 32)
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        return S_OK;
    }
    catch (...)
    {
        return E_FAIL;
    }
}
