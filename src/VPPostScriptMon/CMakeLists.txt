cmake_minimum_required(VERSION 3.10)
project(VPPostScriptMon VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD_17 ON)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(VPPostScriptMon SHARED
    DllMain.cpp
    Monitor.cpp
    Port.cpp
    PipeClient.cpp
)

target_include_directories(VPPostScriptMon PRIVATE .)
target_link_libraries(VPPostScriptMon PRIVATE
    winspool
    kernel32
    user32
    advapi32
)

target_compile_definitions(VPPostScriptMon PRIVATE _UNICODE UNICODE)
