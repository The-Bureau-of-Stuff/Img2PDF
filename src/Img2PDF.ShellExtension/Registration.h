#pragma once

// Local, per-user (HKCU-only, no admin) self-registration for dev/testing before M5's MSIX
// manifest exists. DllRegisterServer/DllUnregisterServer are the classic regsvr32 contract.
HRESULT RegisterShellExtension();
HRESULT UnregisterShellExtension();
