#pragma once

#define WIN32_LEAN_AND_MEAN
#define NOGDI

#include <windows.h>
#include <winspool.h>
#include <winsplp.h>
#include <string>
#include <vector>
#include <mutex>
#include <unordered_map>

#pragma comment(lib, "winspool.lib")
#pragma comment(lib, "advapi32.lib")
