# Arbitrary Picture Format

**APF** -- a lossless image format that doesn't play by the rules.

No DEFLATE. No Huffman trees. No external dependencies. Just raw, scrappy compression
that a microcontroller can decode without breaking a sweat.

```
  PNG: 24,747 bytes
  APF: 13,891 bytes  <-- 44% smaller (circular_image.png)
```

---

## What is this?

APF is a custom lossless image format built from scratch. The encoder is written in
C#/.NET 9 and is deliberately *slow and clever* -- it tries **seven different encoding
strategies** and picks whichever produces the smallest file. The decoder is written in
pure C99 (~875 lines, zero dependencies) and is designed to be fast, simple, and
embeddable on resource-constrained hardware.

**Design philosophy:** Make the encoder do all the hard work so the decoder doesn't have to.

## Features

- **Lossless** -- bit-perfect round-trip, every pixel preserved
- **7 encoding modes** -- the encoder races all strategies and keeps the winner
- **Dual compression** -- RLE, LZ77, rANS, and LZ77+rANS, auto-selected per data stream
- **Z-order curves** -- Morton-code pixel reordering for better spatial locality
- **Arbitrary shapes** -- stencil mask supports non-rectangular image regions
- **Sub-byte packing** -- palette indices packed to minimum bit width (1/2/4/8 bpp)
- **Multi-image files** -- bundle multiple named layers in a single `.apf` (v2.0)
- **Per-image metadata** -- arbitrary key-value pairs stored alongside each image (v1.1+)
- **Paint.NET plugin** -- open, edit, and save APF files directly in Paint.NET (layers + metadata)
- **NuGet package** -- core library available as [`ArbitraryPictureFormat`](https://www.nuget.org/packages/ArbitraryPictureFormat) on NuGet
- **Microcontroller-friendly decoder** -- pure C99, no `malloc` in hot paths, no dependencies to link
- **Built-in viewer** -- Raylib-based C viewer included

## Encoding Strategies

The encoder evaluates all applicable strategies and serializes the smallest:

| Mode | Name | Best For |
|------|------|----------|
| `0` | **Channel Planes** | Photos, gradients -- splits RGBA into independent planes with delta coding |
| `1` | **Palette Indexed** | Icons, pixel art -- up to 256 colors, sub-byte packed indices |
| `2` | **Color Sorted** | Illustrations -- groups pixels by color, stores position deltas |
| `3` | **Solid Fill** | Single-color images -- just 4 bytes |
| `4` | **Mono+Alpha** | Grayscale with transparency -- single luma channel + alpha |
| `5` | **Paeth Full Grid** | Screenshots, UI -- PNG-style Paeth prediction on raw grid |
| `6` | **Paeth Channel Planes** | Non-rectangular images -- 2D Paeth prediction, stores only non-background residuals |

Each mode applies its own transform pipeline, then compresses through the shared
**RLE / LZ77 / rANS** layer (whichever produces the fewest bytes wins).

## Compression Pipeline

```
Raw pixels
    |
    v
+---------------+    +----------------+    +---------------+    +------------------+
|  Z-Order      |--->|  Mode-specific |--->|  Delta        |--->|  RLE / LZ77 /    |
|  Reorder      |    |  Transform     |    |  Encoding     |    |  rANS / LZ77+rANS|
+---------------+    +----------------+    +---------------+    +------------------+
  Morton curves        Channel split        Byte-level          Auto-pick
  for spatial          Paeth predict         differencing        smallest of 4
  locality             Palette map
```

## Binary Format

APF uses a version byte to select the file layout:

### v1.0 -- Single image, no metadata

```
Offset  Size     Field
------  ------   -------------------------
0x00    1        Version (0x10)
0x01    4+4+N    Shape descriptor (width, height, stencil)
 ...    4        Background color (ARGB)
 ...    4        Pixel count
 ...    1        Encoding mode (0-6)
 ...    N        Mode-specific payload
```

### v1.1 -- Single image with metadata

```
Offset  Size     Field
------  ------   -------------------------
0x00    1        Version (0x11)
0x01    4        Metadata entry count
 ...    N×(...)  Key-value pairs (UTF-8 length-prefixed strings)
 ...    ...      Shape descriptor + pixel payload (same as v1.0)
```

### v2.0 -- Multiple named images

```
Offset  Size     Field
------  ------   -------------------------
0x00    1        Version (0x20)
0x01    4        Image count
        Per image:
 ...    4        Name length (UTF-8 bytes)
 ...    N        Name string
 ...    1        Sub-version (0x10 or 0x11)
 ...    N×(...)  Metadata (if sub-version is 0x11)
 ...    ...      Shape descriptor + pixel payload
```

The stencil is a compressed bitmask defining which pixels are "active" vs background.
A sentinel value (rawLen=0, compLen=0) means full rectangular coverage -- no mask needed.

## Project Structure

```
ArbitraryPictureFormat/
|-- ArbitraryPictureFormat.Core/               Core library (NuGet package)
|   |-- ArbitraryPicture.cs                    Encoder/decoder (~1,450 lines)
|   |-- ApfFile.cs                             Multi-image container (v1.0/v1.1/v2.0)
|   +-- ApfImage.cs                            Single image + name + metadata
|
|-- ArbitraryPictureFormat/                    CLI tool
|   +-- Program.cs                             Command-line interface
|
|-- ArbitraryPictureFormat.PaintDotNet/        Paint.NET file type plugin
|   |-- ApfFileType.cs                         Load/save APF files (layers + encoding)
|   |-- ApfFileTypeFactory.cs                  Plugin registration
|   |-- ApfPluginSupportInfo.cs                Plugin metadata
|   +-- ApfMetadataStore.cs                    Shared metadata state for save
|
|-- ArbitraryPictureFormat.PaintDotNet.Effect/ Paint.NET effect plugin
|   |-- ApfMetadataEffect.cs                   Edit per-layer metadata in the UI
|   |-- ApfEffectPluginSupportInfo.cs          Plugin metadata
|   +-- ApfMetadataStore.cs                    Shared metadata state
|
|-- ArbitraryPictureFormat.Tests/              xUnit test suite
|   |-- FileSizeTests.cs                       Regression: APF must stay under size thresholds
|   |-- DiagnosticTests.cs                     Size comparison diagnostics vs PNG/BMP
|   +-- ApfFileTests.cs                        Multi-image, metadata, version round-trips
|
|-- viewer/                                    C99 Raylib viewer (standalone)
|   |-- apf.h                                  Public decoder API
|   |-- apf.c                                  Full APF v1.0 decoder (~875 lines, 0 deps)
|   |-- main.c                                 Raylib window + texture display
|   +-- CMakeLists.txt                         CMake build (auto-fetches Raylib 5.5)
|
|-- data/
|   |-- png/                                   Source images
|   +-- apf/                                   Encoded output
|
+-- ArbitraryPictureFormat.sln                 Visual Studio solution (all projects)
```

## Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download) (encoder/tests)
- [CMake 3.16+](https://cmake.org/) (viewer)
- Visual Studio 2022 or compatible C compiler (viewer)
- [Paint.NET](https://www.getpaint.net/) (optional, for the plugin)

### Convert images

```bash
# PNG to APF
apf photo.png                    # -> photo.apf (same directory)

# APF to PNG
apf sprite.apf                   # -> sprite.png

# Custom output path
apf logo.png -o build/logo.apf

# Decode with stencil mask
apf icon.apf -s                  # -> icon.png + icon_stencil.png

# Extract a named layer from a multi-image APF
apf model.apf -l normal          # -> extract 'normal' layer

# Show file info and metadata
apf -info model.apf              # -> list images and metadata
```

Run via `dotnet run --project ArbitraryPictureFormat -- <args>` or use the
compiled binary directly from `bin/apf.exe`.

### Run tests

```bash
dotnet test ArbitraryPictureFormat.Tests
```

Sixteen tests across three classes: five file-size regression thresholds, a lossless
round-trip check, rANS encoder/decoder validation, a diagnostic report, and seven
multi-image / metadata / version round-trip tests.

### Build & run the viewer

```bash
cd viewer
cmake -B build
cmake --build build --config Release
build\Release\apf_viewer.exe ..\data\apf\circular_image.apf
```

Or just build the entire solution in Visual Studio -- the viewer is included as a
Makefile project that delegates to CMake.

### Paint.NET plugin

Build the `ArbitraryPictureFormat.PaintDotNet` project in Release mode. The DLLs are
copied directly into Paint.NET's `FileTypes\` folder (configurable via `PdnRoot`).
Restart Paint.NET and `.apf` files will appear in the Open/Save dialogs with full
layer and encoding-strategy support.

The companion effect plugin (`ArbitraryPictureFormat.PaintDotNet.Effect`) adds an
"Edit .apf" entry under Effects, allowing per-layer metadata editing before save.

## Size Comparison

| Image | PNG | APF | BMP | APF vs PNG |
|-------|-----|-----|-----|------------|
| circular_image | 24,747 | 13,891 | 921,654 | **0.56x** |
| terminal | 43,977 | 35,713 | 3,686,454 | **0.81x** |
| sample | 22,249 | 21,386 | 921,654 | **0.96x** |
| cow | 420,205 | 441,070 | 2,764,854 | 1.05x |
| rotated_cow | 232,231 | 203,652 | 1,382,454 | **0.88x** |

APF beats PNG on images with large uniform regions, limited palettes, or strong spatial
coherence. The rANS entropy coder now brings photographic images (like rotated_cow)
below PNG size too. Only complex unmasked photos still slightly favor PNG's DEFLATE --
but APF's decoder is *orders of magnitude* simpler.

## Embedding the Decoder

The C decoder (`apf.c` + `apf.h`) is designed to drop into any project:

```c
#include "apf.h"

ApfImage *img = apf_load("sprite.apf");
// img->pixels is RGBA, row-major, width * height
// Use directly as a texture, framebuffer source, etc.
apf_free(img);
```

No dynamic linking. No configuration. Just two files and a C99 compiler.

## License

[Unlicense](LICENSE) -- public domain. Do whatever you want with it.
