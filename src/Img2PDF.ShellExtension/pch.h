#pragma once

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX

#include <windows.h>
#include <shellapi.h>
#include <shlwapi.h>
#include <shobjidl_core.h>
#include <wrl/module.h>
#include <wrl/implements.h>
#include <wrl/client.h>

#include <wil/resource.h>
#include <wil/result_macros.h>

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>
