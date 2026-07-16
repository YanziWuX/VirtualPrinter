cmake_minimum_required(VERSION 3.10)
project(EnvChecker VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(EnvChecker SHARED
    EnvChecker.cpp
)

target_include_directories(EnvChecker PRIVATE .)

target_compile_definitions(EnvChecker PRIVATE _UNICODE UNICODE)
