cmake_minimum_required(VERSION 3.14)
project(apf_viewer C)
set(CMAKE_C_STANDARD 99)

include(FetchContent)
FetchContent_Declare(
    raylib
    GIT_REPOSITORY https://github.com/raysan5/raylib.git
    GIT_TAG        5.5
    GIT_SHALLOW    TRUE
)
FetchContent_MakeAvailable(raylib)

add_executable(apf_viewer main.c apf.c)
target_link_libraries(apf_viewer raylib)

if(MSVC)
    target_compile_options(apf_viewer PRIVATE /W3)
else()
    target_compile_options(apf_viewer PRIVATE -Wall -Wextra)
endif()
