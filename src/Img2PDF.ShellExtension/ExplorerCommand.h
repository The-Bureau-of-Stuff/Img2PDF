#pragma once

#include "pch.h"
#include "Guid.h"

// Deliberately in the global namespace, not Img2PDF::ShellExtension — the WRL CoCreatableClass
// macro below does token-pasting on the class name to build static registration symbols, which
// does not survive a qualified (namespaced) name. This matches the reference implementation
// (microsoft/vscode-explorer-command) exactly for that reason.
// InhibitRoOriginateError avoids pulling in RoOriginateError (runtimeobject.lib) purely for
// WRL's exception-diagnostics path, which we don't use — matches the reference implementation.
class __declspec(uuid("6AC60BE2-CE29-4AEA-8BFF-718F5868B942")) ExplorerCommandHandler final
    : public Microsoft::WRL::RuntimeClass<
        Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom | Microsoft::WRL::InhibitRoOriginateError>,
        IExplorerCommand>
{
public:
    // IExplorerCommand
    IFACEMETHODIMP GetTitle(IShellItemArray* items, PWSTR* name) override;
    IFACEMETHODIMP GetIcon(IShellItemArray* items, PWSTR* icon) override;
    IFACEMETHODIMP GetToolTip(IShellItemArray* items, PWSTR* infoTip) override;
    IFACEMETHODIMP GetCanonicalName(GUID* guidCommandName) override;
    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL okToBeSlow, EXPCMDSTATE* cmdState) override;
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override;
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** enumCommands) override;
    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx* bindCtx) override;
};

CoCreatableClass(ExplorerCommandHandler)
CoCreatableClassWrlCreatorMapInclude(ExplorerCommandHandler)
