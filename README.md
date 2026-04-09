# 🖼️ Arbitrary Picture Format

**APF** — a lossless image format that doesn't play by the rules.

No DEFLATE. No Huffman trees. No external dependencies. Just raw, scrappy compression
that a microcontroller can decode without breaking a sweat.

```
┌──────────────────────────────────────────────────┐
│  PNG: 24,747 bytes                               │
│  APF: 15,240 bytes  ◄── 38% smaller              │
│                         (circular_image.png)      │
└──────────────────────────────────────────────────┘
```

---

## What is this?

APF is a custom lossless image format built from scratch. The encoder is written in
C#/.NET 9 and is deliberately *slow and clever* — it tries **six different encoding
strategies** and picks whichever produces the smallest file. The decoder is written in
pure C99 (~650 lines, zero dependencies) and is designed to be fast, simple, and
embeddable on resource-constrained hardware.

**Design philosophy:** Make the encoder do all the hard work so the decoder doesn't have to.

## Features

- 🔁 **Lossless** — bit-perfect round-trip, every pixel preserved
- 🧠 **6 encoding modes** — the encoder races all strategies and keeps the winner
- 🗜️ **Dual compression** — RLE and LZ77, auto-selected per data stream
- 📐 **Z-order curves** — Morton-code pixel reordering for better spatial locality
- 🎨 **Arbitrary shapes** — stencil mask supports non-rectangular image regions
- 🔬 **Sub-byte packing** — palette indices packed to minimum bit width (1/2/4/8 bpp)
- 📡 **Microcontroller-friendly decoder** — pure C99, no `malloc` in hot paths, no dependencies to link
- 🖥️ **Built-in viewer** — Raylib-based C viewer included

## Encoding Strategies

The encoder evaluates all applicable strategies and serializes the smallest:

| Mode | Name | Best For |
|------|------|----------|
| `0` | **Channel Planes** | Photos, gradients — splits RGBA into independent planes with delta coding |
| `1` | **Palette Indexed** | Icons, pixel art — up to 256 colors, sub-byte packed indices |
| `2` | **Color Sorted** | Illustrations — groups pixels by color, stores position deltas |
| `3` | **Solid Fill** | Single-color images — just 4 bytes |
| `4` | **Mono+Alpha** | Grayscale with transparency — single luma channel + alpha |
| `5` | **Paeth Full Grid** | Screenshots, UI — PNG-style Paeth prediction on raw grid |

Each mode applies its own transform pipeline, then compresses through the shared
**RLE ↔ LZ77** layer (whichever produces fewer bytes wins).

## Compression Pipeline

```
Raw pixels
    │
    ▼
┌─────────────┐    ┌──────────────┐    ┌─────────────┐    ┌──────────┐
│ Z-Order     │───▶│ Mode-specific │───▶│ Delta       │───▶│ RLE or   │
│ Reorder     │    │ Transform     │    │ Encoding    │    │ LZ77     │
└─────────────┘    └──────────────┘    └─────────────┘    └──────────┘
  Morton curves      Channel split       Byte-level         Auto-pick
  for spatial        Paeth predict        differencing       smallest
  locality           Palette map
```

## Binary Format (v1.0)

```
Offset  Size     Field
──────  ──────   ─────────────────────────
0x00    1        Version (0x10 = v1.0)
0x01    4+4+N    Shape descriptor (width, height, stencil)
 ...    4        Background color (ARGB)
 ...    4        Pixel count
 ...    1        Encoding mode (0–5)
 ...    N        Mode-specific payload
```

The stencil is a compressed bitmask defining which pixels are "active" vs background.
A sentinel value (rawLen=0, compLen=0) means full rectangular coverage — no mask needed.

## Project Structure

```
ArbitraryPictureFormat/
├── ArbitraryPictureFormat/          # .NET 9 encoder + decoder library
│   ├── ArbitraryPicture.cs          # The whole format (~1,100 lines)
│   └── Program.cs                   # CLI: PNG → APF + BMP conversion
│
├── ArbitraryPictureFormat.Tests/    # xUnit test suite
│   ├── FileSizeTests.cs             # Regression: APF must stay under size thresholds
│   └── DiagnosticTests.cs           # Size comparison diagnostics vs PNG/BMP
│
├── viewer/                          # C99 Raylib viewer (standalone)
│   ├── apf.h                        # Public decoder API
│   ├── apf.c                        # Full APF v1.0 decoder (~650 lines, 0 deps)
│   ├── main.c                       # Raylib window + texture display
│   └── CMakeLists.txt               # CMake build (auto-fetches Raylib 5.5)
│
├── data/
│   ├── png/                         # Source images
│   └── apf/                         # Encoded output
│
└── ArbitraryPictureFormat.sln       # Visual Studio solution (all projects)
```

## Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download) (encoder/tests)
- [CMake 3.16+](https://cmake.org/) (viewer)
- Visual Studio 2022 or compatible C compiler (viewer)

### Encode images

```bash
dotnet run --project ArbitraryPictureFormat
```

This reads PNGs from `data/png/`, writes APF files to `data/apf/`, and outputs BMP
files alongside for raw size comparison.

### Run tests

```bash
dotnet test ArbitraryPictureFormat.Tests
```

Seven tests: five file-size regression thresholds, a lossless round-trip check, and a
diagnostic report.

### Build & run the viewer

```bash
cd viewer
cmake -B build
cmake --build build --config Release
build\Release\apf_viewer.exe ..\data\apf\circular_image.apf
```

Or just build the entire solution in Visual Studio — the viewer is included as a
Makefile project that delegates to CMake.

## Size Comparison

| Image | PNG | APF | BMP | APF vs PNG |
|-------|-----|-----|-----|------------|
| circular_image | 24,747 | 15,240 | 921,654 | **0.62×** ✅ |
| terminal | 43,977 | 42,645 | 3,686,454 | **0.97×** ✅ |
| sample | 22,249 | 21,461 | 921,654 | **0.96×** ✅ |
| cow | 420,205 | 685,088 | 2,764,854 | 1.63× |
| rotated_cow | 232,231 | 337,827 | 1,382,454 | 1.45× |

APF beats PNG on images with large uniform regions, limited palettes, or strong spatial
coherence. Complex photographic content (like cows 🐄) still favors PNG's DEFLATE — but
APF's decoder is *orders of magnitude* simpler.

## Embedding the Decoder

The C decoder (`apf.c` + `apf.h`) is designed to drop into any project:

```c
#include "apf.h"

ApfImage *img = apf_load("sprite.apf");
// img->pixels is RGBA, row-major, width × height
// Use directly as a texture, framebuffer source, etc.
apf_free(img);
```

No dynamic linking. No configuration. Just two files and a C99 compiler.

## License

[Unlicense](LICENSE) — public domain. Do whatever you want with it.
