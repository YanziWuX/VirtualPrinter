cmake_minimum_required(VERSION 3.10)
project(RedmonRedirector VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_executable(RedmonRedirector main.cpp)

target_link_libraries(RedmonRedirector PRIVATE kernel32 winspool)

target_compile_definitions(RedmonRedirector PRIVATE _UNICODE UNICODE)
