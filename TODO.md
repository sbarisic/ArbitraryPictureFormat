# Arbitrary Picture Format - TODO

A project-specific backlog for APF, a C#/.NET encoder and APF container library with a
Paint.NET integration, xUnit regression tests, and a C99/Raylib decoder-viewer.

> **CPX (Complexity Points)** - 1 to 5 scale:
> - **1** - Single file control/component
> - **2** - Single file control/component with single function change dependencies
> - **3** - Multi-file control/component or single file with multiple dependencies, no architecture changes
> - **4** - Multi-file control/component with multiple dependencies and significant logic, possible minor architecture changes
> - **5** - Large feature spanning multiple components and subsystems, major architecture changes

> Instructions for the TODO list:
> - Move all completed items into separate Completed section
> - Consolidate all completed TODO items by combining similar ones and shortening the descriptions where possible

> How TODO file should be iterated:
> - First handle the Uncategorized section, if any similar issues already are on the TODO list, increase their priority instead of adding duplicates (categorize all at once)
> - When Uncategorized section is empty, start by fixing Active Bugs (take one at a time)
> - After Active Bugs, handle the rest of the TODO file by priority and complexity (High priority takes precedence, then CPX points) (take one at a time).

---

## Features

### High Priority

- [ ] [CPX: 4] Add cross-decoder conformance tests that encode APF files in .NET and verify the C decoder/viewer can decode every supported version and pixel encoding.
- [ ] [CPX: 4] Add CLI support for building and unpacking multi-image APF files, including named layers and per-layer metadata.

### Medium Priority

- [ ] [CPX: 3] Add CLI options to attach metadata during encode and to update metadata in existing APF files without round-tripping through Paint.NET.
- [ ] [CPX: 3] Add stable APF golden fixtures for v1.0, v1.1, v2.0, and all seven pixel encoding modes so backward compatibility is tested independently from the current encoder.
- [ ] [CPX: 2] Expose an in-memory C decoder API alongside `apf_load_file` for embedded callers that already have bytes in flash or RAM.
- [ ] [CPX: 2] Add structured error reporting to the C decoder instead of only returning `NULL`.
- [ ] [CPX: 3] Improve the Raylib viewer with zoom, pan, fit-to-window, transparency checkerboard, and direct layer selection.

### Lower Priority

- [ ] [CPX: 2] Add an APF inspection command that prints the selected pixel encoding, compressed stream modes, stencil size, and payload size breakdown.
- [ ] [CPX: 3] Add a small sample app that demonstrates using the NuGet package to create, inspect, and decode APF files.

---

## Improvements

- [ ] [CPX: 3] Add GitHub Actions or equivalent CI for `dotnet test`, `dotnet pack`, and a C viewer build on at least Windows and one non-MSVC toolchain.
- [ ] [CPX: 4] Add fuzz/corruption tests for the C# and C decoders using malformed headers, invalid lengths, truncated streams, and impossible dimensions.
- [ ] [CPX: 3] Add command-line integration tests for success and failure exit codes, output file creation, `-info`, `-o`, `-s`, and `-l`.
- [ ] [CPX: 2] Report Paint.NET save progress and honor cancellation through the existing `ProgressEventHandler`.
- [ ] [CPX: 3] Package the Paint.NET plugins through a repeatable publish script or artifact folder instead of copying Release builds directly into `C:\Program Files\paint.net`.
- [ ] [CPX: 4] Profile large-image encoding and consider parallelizing encoding candidates or caching generated Z-order tables.
- [ ] [CPX: 2] Replace diagnostic console output in regular tests with `ITestOutputHelper` or mark diagnostics as explicit/manual tests.

---

## Documentation **LOW PRIORITY**

- [ ] [CPX: 3] Write a full binary format specification covering headers, metadata strings, stencil encoding, all pixel modes, and all compression stream modes.
- [ ] [CPX: 2] Add XML API documentation for `ArbitraryPicture`, `ApfFile`, `ApfImage`, `ShapeDesc`, and public helper methods that are intended to remain public.
- [ ] [CPX: 2] Add a focused getting started guide for the CLI, NuGet library, Paint.NET plugin, and C decoder as separate workflows.
- [ ] [CPX: 2] Document platform support clearly: Windows-only image IO via `System.Drawing`, cross-platform stream/container APIs, Paint.NET requirements, and C99 viewer prerequisites.
- [ ] [CPX: 2] Update README viewer details now that the C decoder handles v1.1/v2.0 metadata and multi-image files, not only v1.0 payloads.
- [ ] [CPX: 2] Add troubleshooting notes for Paint.NET install paths, missing Paint.NET references during build, and CMake/Raylib network fetch failures.

---

## Code Cleanup & Technical Debt

### Code Refactoring

- [ ] [CPX: 4] Split `ArbitraryPicture.cs` into focused files for the image model, shape descriptor, pixel encoders, compression helpers, and bitmap interop.
- [ ] [CPX: 2] Deduplicate the two `ApfMetadataStore` implementations used by the Paint.NET file type and effect plugins.
- [ ] [CPX: 3] Replace the Paint.NET metadata temp-file INI format with a structured format that preserves `=`, brackets, newlines, whitespace, duplicate layer names, and renamed layers.
- [ ] [CPX: 2] Update or remove `diag.csx`; it appears stale against the current `ArbitraryPicture.Save(string)` API.
- [ ] [CPX: 1] Remove stale `a.png`, `b.png`, and `c.png` content entries from the CLI project file unless those samples are restored.
- [ ] [CPX: 1] Dispose `Image.FromFile` in the CLI encode path with a `using` statement to avoid keeping source files locked longer than needed.

---

## Known Issues / Bugs

### Active Bugs

- [ ] [MEDIUM] [CPX: 1] Add `PaethChannelPlanes` to the Paint.NET save encoding strategy dropdown so all seven encoder modes are selectable.
- [ ] [MEDIUM] [CPX: 1] Replace `_strdup` in `viewer/apf.c` with a portable helper so the C99 decoder builds under non-MSVC compilers.
- [ ] [MEDIUM] [CPX: 2] Decide whether empty `ApfFile` instances are valid; either reject serialization with a clear exception or fully support zero-image APF files across CLI and C decoder.
- [ ] [MEDIUM] [CPX: 2] Clarify or update `ArbitraryPicture.FromFile`; it currently only accepts v1.0 payload files even though APF container support includes v1.1 and v2.0.

### Uncategorized

*No uncategorized items*

---

## Notes

- Current verification baseline: `dotnet test ArbitraryPictureFormat.Tests` passes 25 tests.
- C viewer build baseline: `cmake --build viewer\build --config Debug --target apf_viewer` succeeds.
- Paint.NET plugin build baseline: both plugin projects build successfully, with existing Paint.NET reference warnings.
- Treat the binary format as compatibility-sensitive: add fixtures before changing serialized bytes, headers, compression mode selection, or metadata layout.
- Keep the C decoder dependency-free and portable C99; viewer-only dependencies such as Raylib should stay outside `apf.c` and `apf.h`.
- Prefer adding tests before codec changes, especially when touching rANS, LZ77, stencil/Z-order mapping, or Paeth channel-plane reconstruction.
- Paint.NET plugin work should be validated against load, edit metadata, save, reopen, and layer-name edge cases.

---

## Completed

### Features

- [x] Implemented APF v1.0 single-image files, v1.1 metadata files, and v2.0 multi-image containers.
- [x] Implemented seven pixel encoding modes with auto-selection.
- [x] Implemented shared RLE, LZ77, rANS, and LZ77+rANS compression selection.
- [x] Implemented CLI encode, decode, stencil export, layer extraction, and file info output.
- [x] Implemented Paint.NET file type and metadata-editing effect plugins.
- [x] Implemented a C99/Raylib APF viewer with multi-image layer navigation.

### Improvements

- [x] Added xUnit coverage for file-size thresholds, lossless round-trips, compression round-trips, metadata, versions, and multi-image containers.
- [x] Added NuGet package metadata for the core library.
- [x] Added Z-order stencil serialization and sub-byte palette index packing.

### Fixed Bugs

- [x] Fixed named layer lookup so missing `-l` selections fail instead of falling back to the first image, with a non-zero CLI exit code on decode failure.
- [x] Added C# APF deserialization validation for malformed lengths, counts, dimensions, truncated reads, and underfilled compressed streams.
- [x] Hardened the C decoder against malformed dimensions, integer overflow, truncated RLE/LZ77/rANS streams, invalid palette/color indices, and oversized allocations.
- [x] Preserved Paint.NET APF metadata across layer renames and duplicate layer names by keying the plugin metadata bridge by layer index with legacy name fallback.
