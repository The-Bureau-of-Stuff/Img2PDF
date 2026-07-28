#pragma once

// {6AC60BE2-CE29-4AEA-8BFF-718F5868B942}
// CLSID for ExplorerCommandHandler. Generated once; must stay stable across builds — it's
// the identity Explorer/registry use to find this handler. Kept as both a GUID and its
// registry-key string form (must match __uuidof(ExplorerCommandHandler) in ExplorerCommand.h)
// so Registration.cpp doesn't need to format one at runtime.
static constexpr GUID CLSID_ExplorerCommandHandler =
{ 0x6ac60be2, 0xce29, 0x4aea, { 0x8b, 0xff, 0x71, 0x8b, 0x58, 0x68, 0xb9, 0x42 } };

inline constexpr wchar_t CLSID_ExplorerCommandHandlerString[] = L"{6AC60BE2-CE29-4AEA-8BFF-718F5868B942}";
