using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
#if WINDOWS7_0_OR_GREATER
using System.Drawing.Imaging;
#endif
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ArbitraryPictureFormat
{
	public enum PixelEncoding : byte
	{
		ChannelPlanes = 0,
		PaletteIndexed = 1,
		ColorSorted = 2,
		SolidFill = 3,
		MonoAlpha = 4,
		PaethFullGrid = 5,
		PaethChannelPlanes = 6,
	}

	public class ArbitraryPicture
	{
		const int BackgroundCandidateLimit = 8;

		readonly struct PayloadCandidate
		{
			public PayloadCandidate(PixelEncoding mode, byte[] data)
			{
				Mode = mode;
				Data = data;
			}

			public PixelEncoding Mode { get; }
			public byte[] Data { get; }
		}

		public Color Background;
		public ShapeDesc Descriptor;
		public Color[] ImageData;

		public static ArbitraryPicture FromFile(string FilePath)
		{
			ApfFile file = ApfFile.FromFile(FilePath);
			if (file.Images.Count != 1)
				throw new InvalidDataException("ArbitraryPicture.FromFile only supports APF files with exactly one image. Use ApfFile.FromFile for multi-image files.");
			return file.Images[0].Picture;
		}

		public ArbitraryPicture(ShapeDesc Descriptor, Color Background)
		{
			this.Background = Background;
			this.Descriptor = Descriptor;
			ImageData = new Color[Descriptor.GetCount()];
		}

#if WINDOWS7_0_OR_GREATER
		public ArbitraryPicture(Image Img)
		{
			using (Bitmap Bmp = new Bitmap(Img))
			{
				BitmapData BmpData = Bmp.LockBits();
				Color[] pixels = new Color[Img.Width * Img.Height];
				for (int y = 0; y < Img.Height; y++)
				{
					for (int x = 0; x < Img.Width; x++)
						pixels[y * Img.Width + x] = BmpData.GetPixel(x, y);
				}

				Bmp.UnlockBits(BmpData);
				InitializeFromPixels(Img.Width, Img.Height, pixels);
			}
		}
#endif

		public ArbitraryPicture(Stream S)
		{
			Deserialize(S);
		}

		public ArbitraryPicture(int width, int height, Color[] pixels)
		{
			if (pixels.Length != width * height)
				throw new ArgumentException("pixels array length must equal width * height");

			InitializeFromPixels(width, height, pixels);
		}

		void InitializeFromPixels(int width, int height, Color[] pixels)
		{
			Dictionary<int, int> colorCounts = CountColors(pixels);
			Background = ChooseBackgroundColor(width, height, pixels, colorCounts);
			(Descriptor, ImageData) = BuildStorage(width, height, pixels, Background);
		}

		static Dictionary<int, int> CountColors(Color[] pixels)
		{
			var colorCounts = new Dictionary<int, int>();
			for (int i = 0; i < pixels.Length; i++)
			{
				int argb = pixels[i].ToArgb();
				if (colorCounts.ContainsKey(argb))
					colorCounts[argb]++;
				else
					colorCounts[argb] = 1;
			}

			return colorCounts;
		}

		static Color ChooseBackgroundColor(int width, int height, Color[] pixels, Dictionary<int, int> colorCounts)
		{
			if (pixels.Length == 0)
				return Color.Transparent;

			var candidateArgbs = colorCounts
				.OrderByDescending(kvp => kvp.Value)
				.ThenBy(kvp => kvp.Key)
				.Take(Math.Min(BackgroundCandidateLimit, colorCounts.Count))
				.Select(kvp => kvp.Key)
				.ToList();

			if (TryGetUnusedBackgroundArgb(colorCounts, out int unusedArgb))
				candidateArgbs.Add(unusedArgb);

			long bestSize = long.MaxValue;
			int bestArgb = candidateArgbs[0];
			int bestCount = colorCounts.GetValueOrDefault(bestArgb);

			for (int i = 0; i < candidateArgbs.Count; i++)
			{
				int argb = candidateArgbs[i];
				long size = EstimateSerializedSize(width, height, pixels, Color.FromArgb(argb));
				int count = colorCounts.GetValueOrDefault(argb);
				if (size < bestSize || (size == bestSize && count > bestCount) || (size == bestSize && count == bestCount && argb < bestArgb))
				{
					bestSize = size;
					bestArgb = argb;
					bestCount = count;
				}
			}

			return Color.FromArgb(bestArgb);
		}

		static bool TryGetUnusedBackgroundArgb(Dictionary<int, int> colorCounts, out int argb)
		{
			int[] preferred = new[]
			{
				unchecked((int)0x00000000),
				unchecked((int)0xFFFFFFFF),
				unchecked((int)0xFF000000),
				unchecked((int)0xFFFF00FF),
				unchecked((int)0xFF00FFFF),
			};

			for (int i = 0; i < preferred.Length; i++)
			{
				if (!colorCounts.ContainsKey(preferred[i]))
				{
					argb = preferred[i];
					return true;
				}
			}

			for (int candidate = 0; candidate < int.MaxValue; candidate++)
			{
				if (!colorCounts.ContainsKey(candidate))
				{
					argb = candidate;
					return true;
				}
			}

			argb = 0;
			return false;
		}

		static (ShapeDesc descriptor, Color[] imageData) BuildStorage(int width, int height, Color[] pixels, Color background)
		{
			ShapeDesc descriptor = new ShapeDesc(width, height);
			int shapePixelCount = 0;
			for (int y = 0; y < height; y++)
				for (int x = 0; x < width; x++)
				{
					bool inShape = pixels[y * width + x] != background;
					descriptor.Set(x, y, inShape);
					if (inShape)
						shapePixelCount++;
				}

			Color[] imageData = new Color[shapePixelCount];
			int idx = 0;
			for (int y = 0; y < height; y++)
				for (int x = 0; x < width; x++)
				{
					Color pixel = pixels[y * width + x];
					if (pixel != background)
						imageData[idx++] = pixel;
				}

			return (descriptor, imageData);
		}

		static long EstimateSerializedSize(int width, int height, Color[] pixels, Color background)
		{
			(ShapeDesc descriptor, Color[] imageData) = BuildStorage(width, height, pixels, background);
			var picture = new ArbitraryPicture(descriptor, background)
			{
				ImageData = imageData
			};
			ArbitraryPictureEncodingAnalysis analysis = picture.AnalyzeEncoding();
			return 1L + analysis.Stencil.SerializedSize + 4 + 4 + analysis.SelectedCandidate.PayloadSize;
		}

#if WINDOWS7_0_OR_GREATER
		public Bitmap ToStencilBitmap()
		{
			Bitmap Bmp = new Bitmap(Descriptor.Width, Descriptor.Height);
			BitmapData BmpData = Bmp.LockBits();

			for (int y = 0; y < Descriptor.Height; y++)
				for (int x = 0; x < Descriptor.Width; x++)
					BmpData.SetPixel(x, y, Descriptor.Get(x, y) ? Color.White : Color.Black);

			Bmp.UnlockBits(BmpData);
			return Bmp;
		}

		public Bitmap ToBitmap(Color Background)
		{
			Bitmap Bmp = new Bitmap(Descriptor.Width, Descriptor.Height);
			BitmapData BmpData = Bmp.LockBits();

			int Idx = 0;
			for (int y = 0; y < Descriptor.Height; y++)
				for (int x = 0; x < Descriptor.Width; x++)
				{
					BmpData.SetPixel(x, y, Descriptor.Get(x, y) ? ImageData[Idx++] : Background);
				}

			Bmp.UnlockBits(BmpData);
			return Bmp;
		}

		public Bitmap ToBitmap()
		{
			return ToBitmap(Color.Transparent);
		}
#endif

		public void Save(string FilePath)
		{
			using (FileStream FS = File.Create(FilePath))
				Serialize(FS);
		}

		const byte FORMAT_VERSION = 0x10; // v1.0

		HashSet<int> GetUniqueArgbSet()
		{
			var set = new HashSet<int>();
			for (int i = 0; i < ImageData.Length; i++)
				set.Add(ImageData[i].ToArgb());
			return set;
		}

		bool IsSolidFill()
		{
			if (ImageData.Length <= 1) return true;
			int first = ImageData[0].ToArgb();
			for (int i = 1; i < ImageData.Length; i++)
				if (ImageData[i].ToArgb() != first) return false;
			return true;
		}

		bool IsMonochrome()
		{
			for (int i = 0; i < ImageData.Length; i++)
			{
				Color c = ImageData[i];
				if (c.R != c.G || c.R != c.B) return false;
			}
			return true;
		}

		public void Serialize(Stream S) => Serialize(S, null);

		public void Serialize(Stream S, PixelEncoding? forcedEncoding)
		{
			using (BinaryWriter Writer = new BinaryWriter(S, Encoding.UTF8, true))
			{
				Writer.Write(FORMAT_VERSION);
				SerializePayload(Writer, forcedEncoding);
			}
		}

		public void SerializePayload(BinaryWriter Writer, PixelEncoding? forcedEncoding)
		{
			Descriptor.Serialize(Writer);
			Writer.Write(Background.ToArgb());
			Writer.Write(ImageData.Length);

			int[] zOrder = Helpers.GenerateZOrderIndices(Descriptor.Width, Descriptor.Height);
			Color[] zPixels = ReorderPixelsToZOrder(zOrder);
			List<PayloadCandidate> candidates = BuildPayloadCandidates(zPixels, forcedEncoding);
			int bestIndex = SelectBestCandidateIndex(candidates);

			Writer.Write((byte)candidates[bestIndex].Mode);
			Writer.Write(candidates[bestIndex].Data);
		}

		public ArbitraryPictureEncodingAnalysis AnalyzeEncoding(PixelEncoding? forcedEncoding = null)
		{
			int[] zOrder = Helpers.GenerateZOrderIndices(Descriptor.Width, Descriptor.Height);
			Color[] zPixels = ReorderPixelsToZOrder(zOrder);
			List<PayloadCandidate> candidates = BuildPayloadCandidates(zPixels, forcedEncoding);
			int bestIndex = SelectBestCandidateIndex(candidates);

			var analyses = new List<PixelEncodingCandidateAnalysis>(candidates.Count);
			for (int i = 0; i < candidates.Count; i++)
			{
				analyses.Add(new PixelEncodingCandidateAnalysis(
					candidates[i].Mode,
					1 + candidates[i].Data.Length,
					i == bestIndex,
					AnalyzePayloadCandidate(candidates[i].Mode, candidates[i].Data)));
			}

			return new ArbitraryPictureEncodingAnalysis(
				Background,
				ApfReadHelpers.CheckedElementCount(Descriptor.Width, Descriptor.Height),
				ImageData.Length,
				Descriptor.AnalyzeEncoding(),
				analyses,
				analyses[bestIndex]);
		}

		Color[] ReorderPixelsToZOrder(int[] zOrder)
		{
			int[] scanToImg = new int[Descriptor.Width * Descriptor.Height];
			int imgIdx = 0;
			for (int i = 0; i < scanToImg.Length; i++)
				scanToImg[i] = Descriptor.Data[i] ? imgIdx++ : -1;

			Color[] zPixels = new Color[ImageData.Length];
			int zi = 0;
			for (int i = 0; i < zOrder.Length; i++)
			{
				int si = scanToImg[zOrder[i]];
				if (si >= 0)
					zPixels[zi++] = ImageData[si];
			}
			return zPixels;
		}

		void ReorderPixelsFromZOrder(int[] zOrder, Color[] zPixels)
		{
			int[] scanToImg = new int[Descriptor.Width * Descriptor.Height];
			int imgIdx = 0;
			for (int i = 0; i < scanToImg.Length; i++)
				scanToImg[i] = Descriptor.Data[i] ? imgIdx++ : -1;

			ImageData = new Color[zPixels.Length];
			int zi = 0;
			for (int i = 0; i < zOrder.Length; i++)
			{
				int si = scanToImg[zOrder[i]];
				if (si >= 0)
					ImageData[si] = zPixels[zi++];
			}
		}

		List<PayloadCandidate> BuildPayloadCandidates(Color[] zPixels, PixelEncoding? forcedEncoding)
		{
			var candidates = new List<PayloadCandidate>();

			void addDefaultCandidates()
			{
				candidates.Add(new PayloadCandidate(PixelEncoding.ChannelPlanes, EncodeChannelPlanes(zPixels)));

				if (ImageData.Length == 0 || IsSolidFill())
					candidates.Add(new PayloadCandidate(PixelEncoding.SolidFill, EncodeSolidFill()));

				HashSet<int> uniqueArgbs = GetUniqueArgbSet();
				if (uniqueArgbs.Count <= 256 && uniqueArgbs.Count > 0)
					candidates.Add(new PayloadCandidate(PixelEncoding.PaletteIndexed, EncodePaletteIndexed(zPixels, uniqueArgbs)));

				if (IsMonochrome())
					candidates.Add(new PayloadCandidate(PixelEncoding.MonoAlpha, EncodeMonoAlpha(zPixels)));

				if (ImageData.Length > 0)
					candidates.Add(new PayloadCandidate(PixelEncoding.ColorSorted, EncodeColorSorted()));

				candidates.Add(new PayloadCandidate(PixelEncoding.PaethFullGrid, EncodePaethFullGrid()));
				candidates.Add(new PayloadCandidate(PixelEncoding.PaethChannelPlanes, EncodePaethChannelPlanes()));
			}

			if (forcedEncoding == null)
			{
				addDefaultCandidates();
				return candidates;
			}

			bool added = false;
			switch (forcedEncoding.Value)
			{
				case PixelEncoding.ChannelPlanes:
					candidates.Add(new PayloadCandidate(PixelEncoding.ChannelPlanes, EncodeChannelPlanes(zPixels)));
					added = true;
					break;
				case PixelEncoding.SolidFill:
					if (ImageData.Length == 0 || IsSolidFill())
					{
						candidates.Add(new PayloadCandidate(PixelEncoding.SolidFill, EncodeSolidFill()));
						added = true;
					}
					break;
				case PixelEncoding.PaletteIndexed:
					HashSet<int> ua = GetUniqueArgbSet();
					if (ua.Count <= 256 && ua.Count > 0)
					{
						candidates.Add(new PayloadCandidate(PixelEncoding.PaletteIndexed, EncodePaletteIndexed(zPixels, ua)));
						added = true;
					}
					break;
				case PixelEncoding.MonoAlpha:
					if (IsMonochrome())
					{
						candidates.Add(new PayloadCandidate(PixelEncoding.MonoAlpha, EncodeMonoAlpha(zPixels)));
						added = true;
					}
					break;
				case PixelEncoding.ColorSorted:
					if (ImageData.Length > 0)
					{
						candidates.Add(new PayloadCandidate(PixelEncoding.ColorSorted, EncodeColorSorted()));
						added = true;
					}
					break;
				case PixelEncoding.PaethFullGrid:
					candidates.Add(new PayloadCandidate(PixelEncoding.PaethFullGrid, EncodePaethFullGrid()));
					added = true;
					break;
				case PixelEncoding.PaethChannelPlanes:
					candidates.Add(new PayloadCandidate(PixelEncoding.PaethChannelPlanes, EncodePaethChannelPlanes()));
					added = true;
					break;
			}

			if (!added)
				addDefaultCandidates();

			return candidates;
		}

		static int SelectBestCandidateIndex(List<PayloadCandidate> candidates)
		{
			int bestIndex = 0;
			int bestSize = candidates[0].Data.Length;
			for (int i = 1; i < candidates.Count; i++)
			{
				if (candidates[i].Data.Length < bestSize)
				{
					bestIndex = i;
					bestSize = candidates[i].Data.Length;
				}
			}

			return bestIndex;
		}

		List<PayloadComponentAnalysis> AnalyzePayloadCandidate(PixelEncoding mode, byte[] data)
		{
			int totalPixelCount = ApfReadHelpers.CheckedElementCount(Descriptor.Width, Descriptor.Height);
			int shapePixelCount = ImageData.Length;

			using (var ms = new MemoryStream(data, false))
			using (var reader = new BinaryReader(ms))
			{
				var components = new List<PayloadComponentAnalysis>();
				switch (mode)
				{
					case PixelEncoding.ChannelPlanes:
						{
							byte flags = reader.ReadByte();
							bool isMono = (flags & 1) != 0;
							bool hasR = (flags & 2) != 0;
							bool hasG = (flags & 4) != 0;
							bool hasB = (flags & 8) != 0;
							bool hasA = (flags & 16) != 0;
							byte defR = reader.ReadByte();
							byte defG = reader.ReadByte();
							byte defB = reader.ReadByte();
							byte defA = reader.ReadByte();

							components.Add(new PayloadComponentAnalysis(
								"Header",
								5,
								5,
								details: $"flags=0x{flags:X2}, defaults={FormatArgb(defA, defR, defG, defB)}"));

							if (isMono)
							{
								if (hasR)
									components.Add(ReadCompressedComponent(reader, "Luma plane", "delta", shapePixelCount));
							}
							else
							{
								if (hasR)
									components.Add(ReadCompressedComponent(reader, "Red plane", "delta", shapePixelCount));
								if (hasG)
									components.Add(ReadCompressedComponent(reader, "Green plane", "delta", shapePixelCount));
								if (hasB)
									components.Add(ReadCompressedComponent(reader, "Blue plane", "delta", shapePixelCount));
							}

							if (hasA)
								components.Add(ReadCompressedComponent(reader, "Alpha plane", "delta", shapePixelCount));
							break;
						}

					case PixelEncoding.SolidFill:
						components.Add(new PayloadComponentAnalysis(
							"Solid color",
							4,
							4,
							details: FormatArgb(reader.ReadInt32())));
						break;

					case PixelEncoding.PaletteIndexed:
						{
							ushort paletteLength = reader.ReadUInt16();
							for (int i = 0; i < paletteLength; i++)
								reader.ReadInt32();

							byte bitsPerIndex = reader.ReadByte();
							components.Add(new PayloadComponentAnalysis(
								"Palette",
								2 + (paletteLength * 4),
								2 + (paletteLength * 4),
								details: $"{paletteLength} colors"));

							int packedLength = CheckedPackedLength(shapePixelCount, bitsPerIndex);
							components.Add(ReadCompressedComponent(
								reader,
								"Index stream",
								"delta",
								packedLength,
								$"{bitsPerIndex} bits/index"));
							break;
						}

					case PixelEncoding.MonoAlpha:
						{
							components.Add(ReadCompressedComponent(reader, "Luma plane", "delta", shapePixelCount));
							byte alphaMode = reader.ReadByte();
							components.Add(new PayloadComponentAnalysis(
								"Alpha selector",
								1,
								1,
								details: alphaMode == 0 ? "constant alpha" : "per-pixel alpha"));
							if (alphaMode != 0)
								components.Add(ReadCompressedComponent(reader, "Alpha plane", "delta", shapePixelCount));
							else
								components.Add(new PayloadComponentAnalysis(
									"Alpha constant",
									1,
									1,
									details: reader.ReadByte().ToString()));
							break;
						}

					case PixelEncoding.ColorSorted:
						{
							int uniqueCount = reader.ReadInt32();
							for (int i = 0; i < uniqueCount; i++)
								reader.ReadInt32();
							for (int i = 0; i < uniqueCount; i++)
								reader.ReadInt32();
							byte posWidth = reader.ReadByte();

							components.Add(new PayloadComponentAnalysis(
								"Color table and counts",
								4 + (uniqueCount * 4) + (uniqueCount * 4) + 1,
								4 + (uniqueCount * 4) + (uniqueCount * 4) + 1,
								details: $"{uniqueCount} colors, {posWidth}-byte deltas"));
							components.Add(ReadCompressedComponent(reader, "Position deltas", "none", shapePixelCount * posWidth));
							break;
						}

					case PixelEncoding.PaethFullGrid:
						{
							byte flags = reader.ReadByte();
							bool hasR = (flags & 1) != 0;
							bool hasG = (flags & 2) != 0;
							bool hasB = (flags & 4) != 0;
							bool hasA = (flags & 8) != 0;
							bool isMono = (flags & 16) != 0;
							components.Add(new PayloadComponentAnalysis("Channel flags", 1, 1, details: $"0x{flags:X2}"));
							components.Add(ReadPaethPlaneComponent(reader, "Red plane", hasR, totalPixelCount));
							if (isMono)
								components.Add(new PayloadComponentAnalysis("Green/Blue reuse", 0, 0, details: "monochrome RGB"));
							else
							{
								components.Add(ReadPaethPlaneComponent(reader, "Green plane", hasG, totalPixelCount));
								components.Add(ReadPaethPlaneComponent(reader, "Blue plane", hasB, totalPixelCount));
							}
							components.Add(ReadPaethPlaneComponent(reader, "Alpha plane", hasA, totalPixelCount));
							break;
						}

					case PixelEncoding.PaethChannelPlanes:
						{
							byte flags = reader.ReadByte();
							bool hasR = (flags & 1) != 0;
							bool hasG = (flags & 2) != 0;
							bool hasB = (flags & 4) != 0;
							bool hasA = (flags & 8) != 0;
							bool isMono = (flags & 16) != 0;
							components.Add(new PayloadComponentAnalysis("Channel flags", 1, 1, details: $"0x{flags:X2}"));
							components.Add(ReadPaethPlaneComponent(reader, "Red stencil residuals", hasR, shapePixelCount));
							if (isMono)
								components.Add(new PayloadComponentAnalysis("Green/Blue reuse", 0, 0, details: "monochrome RGB"));
							else
							{
								components.Add(ReadPaethPlaneComponent(reader, "Green stencil residuals", hasG, shapePixelCount));
								components.Add(ReadPaethPlaneComponent(reader, "Blue stencil residuals", hasB, shapePixelCount));
							}
							components.Add(ReadPaethPlaneComponent(reader, "Alpha stencil residuals", hasA, shapePixelCount));
							break;
						}
				}

				if (ms.Position != ms.Length)
					throw new InvalidDataException($"Payload analysis for {mode} left unread bytes behind.");

				return components;
			}
		}

		static PayloadComponentAnalysis ReadCompressedComponent(BinaryReader reader, string name, string transform, int rawSize, string details = null)
		{
			int length = reader.ReadInt32();
			byte[] compressed = reader.ReadBytes(length);
			if (compressed.Length != length)
				throw new EndOfStreamException("Truncated payload component during analysis.");
			return new PayloadComponentAnalysis(
				name,
				rawSize,
				4 + length,
				transform,
				AnalyzeCompression(compressed, rawSize),
				details);
		}

		static PayloadComponentAnalysis ReadPaethPlaneComponent(BinaryReader reader, string name, bool compressed, int rawSize)
		{
			if (compressed)
				return ReadCompressedComponent(reader, name, "Paeth", rawSize);

			return new PayloadComponentAnalysis(
				name,
				1,
				1,
				details: $"constant={reader.ReadByte()}");
		}

		static CompressionAnalysis AnalyzeCompression(byte[] compressed, int rawSize)
		{
			if (compressed.Length == 0)
				throw new InvalidDataException("Compressed component is empty.");

			return new CompressionAnalysis(GetCompressionMode(compressed[0]), rawSize, compressed.Length);
		}

		static CompressionMode GetCompressionMode(byte mode)
		{
			return mode switch
			{
				0 => CompressionMode.Rle,
				1 => CompressionMode.Lz77,
				2 => CompressionMode.Rans,
				3 => CompressionMode.Lz77Rans,
				_ => throw new InvalidDataException("Unknown compression mode in analyzed payload: " + mode),
			};
		}

		static string FormatArgb(int argb)
		{
			Color c = Color.FromArgb(argb);
			return FormatArgb(c.A, c.R, c.G, c.B);
		}

		static string FormatArgb(byte a, byte r, byte g, byte b)
		{
			return $"#{a:X2}{r:X2}{g:X2}{b:X2}";
		}

		// --- Encode methods ---

		static void WriteCompressedPlane(BinaryWriter w, byte[] plane)
		{
			byte[] delta = Helpers.DeltaEncode(plane);
			byte[] compressed = Helpers.Compress(delta);
			w.Write(compressed.Length);
			w.Write(compressed);
		}

		static byte[] ReadCompressedPlane(BinaryReader r, int pixelCount)
		{
			int len = r.ReadInt32();
			byte[] compressed = ApfReadHelpers.ReadBytesExact(r, len, "compressed plane");
			byte[] delta = Helpers.Decompress(compressed, pixelCount);
			return Helpers.DeltaDecode(delta);
		}

		byte[] EncodeChannelPlanes(Color[] zPixels)
		{
			// Determine which channels vary
			bool isMono = true;
			byte defR = zPixels.Length > 0 ? zPixels[0].R : (byte)0;
			byte defG = zPixels.Length > 0 ? zPixels[0].G : (byte)0;
			byte defB = zPixels.Length > 0 ? zPixels[0].B : (byte)0;
			byte defA = zPixels.Length > 0 ? zPixels[0].A : (byte)0;
			bool hasR = false, hasG = false, hasB = false, hasA = false;

			for (int i = 0; i < zPixels.Length; i++)
			{
				Color c = zPixels[i];
				if (c.R != c.G || c.R != c.B) isMono = false;
				if (c.R != defR) hasR = true;
				if (c.G != defG) hasG = true;
				if (c.B != defB) hasB = true;
				if (c.A != defA) hasA = true;
			}

			byte flags = (byte)(
			(isMono ? 1 : 0) |
			(hasR ? 2 : 0) |
			(hasG ? 4 : 0) |
			(hasB ? 8 : 0) |
			(hasA ? 16 : 0));

			using (var ms = new MemoryStream())
			using (var w = new BinaryWriter(ms))
			{
				w.Write(flags);
				w.Write(defR);
				w.Write(defG);
				w.Write(defB);
				w.Write(defA);

				if (isMono)
				{
					if (hasR)
					{
						byte[] plane = new byte[zPixels.Length];
						for (int i = 0; i < zPixels.Length; i++) plane[i] = zPixels[i].R;
						WriteCompressedPlane(w, plane);
					}
				}
				else
				{
					if (hasR)
					{
						byte[] plane = new byte[zPixels.Length];
						for (int i = 0; i < zPixels.Length; i++) plane[i] = zPixels[i].R;
						WriteCompressedPlane(w, plane);
					}
					if (hasG)
					{
						byte[] plane = new byte[zPixels.Length];
						for (int i = 0; i < zPixels.Length; i++) plane[i] = zPixels[i].G;
						WriteCompressedPlane(w, plane);
					}
					if (hasB)
					{
						byte[] plane = new byte[zPixels.Length];
						for (int i = 0; i < zPixels.Length; i++) plane[i] = zPixels[i].B;
						WriteCompressedPlane(w, plane);
					}
				}
				if (hasA)
				{
					byte[] plane = new byte[zPixels.Length];
					for (int i = 0; i < zPixels.Length; i++) plane[i] = zPixels[i].A;
					WriteCompressedPlane(w, plane);
				}

				return ms.ToArray();
			}
		}

		byte[] EncodeSolidFill()
		{
			using (var ms = new MemoryStream())
			using (var w = new BinaryWriter(ms))
			{
				w.Write(ImageData.Length > 0 ? ImageData[0].ToArgb() : 0);
				return ms.ToArray();
			}
		}

		byte[] EncodePaletteIndexed(Color[] zPixels, HashSet<int> uniqueArgbs)
		{
			int[] palette = uniqueArgbs.OrderBy(x => x).ToArray();
			var indexMap = new Dictionary<int, byte>();
			for (int i = 0; i < palette.Length; i++)
				indexMap[palette[i]] = (byte)i;

			byte[] indices = new byte[zPixels.Length];
			for (int i = 0; i < zPixels.Length; i++)
				indices[i] = indexMap[zPixels[i].ToArgb()];

			int count = palette.Length;
			byte bitsPerIndex;
			if (count <= 2) bitsPerIndex = 1;
			else if (count <= 4) bitsPerIndex = 2;
			else if (count <= 16) bitsPerIndex = 4;
			else bitsPerIndex = 8;

			using (var ms = new MemoryStream())
			using (var w = new BinaryWriter(ms))
			{
				w.Write((ushort)palette.Length);
				for (int i = 0; i < palette.Length; i++)
					w.Write(palette[i]);

				w.Write(bitsPerIndex);

				byte[] packed = bitsPerIndex == 8 ? indices : Helpers.PackBits(indices, bitsPerIndex);
				byte[] delta = Helpers.DeltaEncode(packed);
				byte[] compressed = Helpers.Compress(delta);
				w.Write(compressed.Length);
				w.Write(compressed);
				return ms.ToArray();
			}
		}

		byte[] EncodeMonoAlpha(Color[] zPixels)
		{
			byte[] luma = new byte[zPixels.Length];
			byte[] alpha = new byte[zPixels.Length];
			bool alphaVaries = false;
			byte firstAlpha = zPixels.Length > 0 ? zPixels[0].A : (byte)255;

			for (int i = 0; i < zPixels.Length; i++)
			{
				luma[i] = zPixels[i].R;
				alpha[i] = zPixels[i].A;
				if (alpha[i] != firstAlpha) alphaVaries = true;
			}

			using (var ms = new MemoryStream())
			using (var w = new BinaryWriter(ms))
			{
				WriteCompressedPlane(w, luma);
				w.Write(alphaVaries ? (byte)1 : (byte)0);
				if (alphaVaries)
					WriteCompressedPlane(w, alpha);
				else
					w.Write(firstAlpha);
				return ms.ToArray();
			}
		}

		byte[] EncodeColorSorted()
		{
			var colorPositions = new SortedDictionary<int, List<int>>();
			for (int i = 0; i < ImageData.Length; i++)
			{
				int argb = ImageData[i].ToArgb();
				if (!colorPositions.ContainsKey(argb))
					colorPositions[argb] = new List<int>();
				colorPositions[argb].Add(i);
			}

			int uniqueCount = colorPositions.Count;
			int[] sortedColors = new int[uniqueCount];
			int[] counts = new int[uniqueCount];
			var allPositionDeltas = new List<int>();

			int ci = 0;
			foreach (var kvp in colorPositions)
			{
				sortedColors[ci] = kvp.Key;
				counts[ci] = kvp.Value.Count;
				ci++;

				List<int> positions = kvp.Value;
				allPositionDeltas.Add(positions[0]);
				for (int i = 1; i < positions.Count; i++)
					allPositionDeltas.Add(positions[i] - positions[i - 1]);
			}

			int maxVal = 0;
			for (int i = 0; i < allPositionDeltas.Count; i++)
				if (allPositionDeltas[i] > maxVal) maxVal = allPositionDeltas[i];

			byte posWidth;
			if (maxVal <= 0xFF) posWidth = 1;
			else if (maxVal <= 0xFFFF) posWidth = 2;
			else posWidth = 4;

			byte[] posBytes = Helpers.IntsToBytes(allPositionDeltas.ToArray(), posWidth);

			using (var ms = new MemoryStream())
			using (var w = new BinaryWriter(ms))
			{
				w.Write(uniqueCount);
				for (int i = 0; i < uniqueCount; i++)
					w.Write(sortedColors[i]);
				for (int i = 0; i < uniqueCount; i++)
					w.Write(counts[i]);

				w.Write(posWidth);
				byte[] compressed = Helpers.Compress(posBytes);
				w.Write(compressed.Length);
				w.Write(compressed);
				return ms.ToArray();
			}
		}

		byte[] EncodePaethFullGrid()
		{
			int w = Descriptor.Width, h = Descriptor.Height;
			byte[] rPlane = new byte[w * h];
			byte[] gPlane = new byte[w * h];
			byte[] bPlane = new byte[w * h];
			byte[] aPlane = new byte[w * h];

			int idx = 0;
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					int i = y * w + x;
					Color c = Descriptor.Get(x, y) ? ImageData[idx++] : Background;
					rPlane[i] = c.R;
					gPlane[i] = c.G;
					bPlane[i] = c.B;
					aPlane[i] = c.A;
				}
			}

			bool isMono = true;
			for (int i = 0; i < w * h; i++)
				if (rPlane[i] != gPlane[i] || rPlane[i] != bPlane[i]) { isMono = false; break; }

			bool hasR = !rPlane.IsHomogenous();
			bool hasG = !isMono && !gPlane.IsHomogenous();
			bool hasB = !isMono && !bPlane.IsHomogenous();
			bool hasA = !aPlane.IsHomogenous();

			byte channelFlags = (byte)(
			(hasR ? 1 : 0) | (hasG ? 2 : 0) | (hasB ? 4 : 0) |
			(hasA ? 8 : 0) | (isMono ? 16 : 0));

			using (var ms = new MemoryStream())
			using (var wr = new BinaryWriter(ms))
			{
				wr.Write(channelFlags);

				if (hasR) WritePaethPlane(wr, rPlane, w, h); else wr.Write(rPlane[0]);
				if (!isMono)
				{
					if (hasG) WritePaethPlane(wr, gPlane, w, h); else wr.Write(gPlane[0]);
					if (hasB) WritePaethPlane(wr, bPlane, w, h); else wr.Write(bPlane[0]);
				}
				if (hasA) WritePaethPlane(wr, aPlane, w, h); else wr.Write(aPlane[0]);

				return ms.ToArray();
			}
		}

		static void WritePaethPlane(BinaryWriter w, byte[] plane, int width, int height)
		{
			byte[] residuals = Helpers.PaethEncode(plane, width, height);
			byte[] compressed = Helpers.Compress(residuals);
			w.Write(compressed.Length);
			w.Write(compressed);
		}

		static byte[] ReadPaethPlane(BinaryReader r, int width, int height)
		{
			int len = r.ReadInt32();
			byte[] compressed = ApfReadHelpers.ReadBytesExact(r, len, "Paeth plane");
			byte[] residuals = Helpers.Decompress(compressed, ApfReadHelpers.CheckedElementCount(width, height));
			return Helpers.PaethDecode(residuals, width, height);
		}

		byte[] EncodePaethChannelPlanes()
		{
			int w = Descriptor.Width, h = Descriptor.Height;
			int total = w * h;
			int pixelCount = ImageData.Length;

			// Reconstruct full grid per channel
			byte[] rFull = new byte[total];
			byte[] gFull = new byte[total];
			byte[] bFull = new byte[total];
			byte[] aFull = new byte[total];

			int idx = 0;
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					int i = y * w + x;
					Color c = Descriptor.Get(x, y) ? ImageData[idx++] : Background;
					rFull[i] = c.R;
					gFull[i] = c.G;
					bFull[i] = c.B;
					aFull[i] = c.A;
				}
			}

			bool isMono = true;
			for (int i = 0; i < total; i++)
				if (rFull[i] != gFull[i] || rFull[i] != bFull[i]) { isMono = false; break; }

			bool hasR = !rFull.IsHomogenous();
			bool hasG = !isMono && !gFull.IsHomogenous();
			bool hasB = !isMono && !bFull.IsHomogenous();
			bool hasA = !aFull.IsHomogenous();

			byte channelFlags = (byte)(
				(hasR ? 1 : 0) | (hasG ? 2 : 0) | (hasB ? 4 : 0) |
				(hasA ? 8 : 0) | (isMono ? 16 : 0));

			using (var ms = new MemoryStream())
			using (var wr = new BinaryWriter(ms))
			{
				wr.Write(channelFlags);

				if (hasR) WritePaethChannelPlane(wr, rFull, w, h, pixelCount);
				else wr.Write(rFull[0]);

				if (!isMono)
				{
					if (hasG) WritePaethChannelPlane(wr, gFull, w, h, pixelCount);
					else wr.Write(gFull[0]);
					if (hasB) WritePaethChannelPlane(wr, bFull, w, h, pixelCount);
					else wr.Write(bFull[0]);
				}

				if (hasA) WritePaethChannelPlane(wr, aFull, w, h, pixelCount);
				else wr.Write(aFull[0]);

				return ms.ToArray();
			}
		}

		// Paeth-encode full grid, then extract and compress only stencil-true residuals
		void WritePaethChannelPlane(BinaryWriter wr, byte[] fullPlane, int width, int height, int pixelCount)
		{
			byte[] fullResiduals = Helpers.PaethEncode(fullPlane, width, height);

			byte[] stencilResiduals = new byte[pixelCount];
			int si = 0;
			for (int y = 0; y < height; y++)
				for (int x = 0; x < width; x++)
					if (Descriptor.Get(x, y))
						stencilResiduals[si++] = fullResiduals[y * width + x];

			byte[] compressed = Helpers.Compress(stencilResiduals);
			wr.Write(compressed.Length);
			wr.Write(compressed);
		}

		void DecodePaethChannelPlanes(BinaryReader Reader, int pixelCount)
		{
			int w = Descriptor.Width, h = Descriptor.Height;
			int total = ApfReadHelpers.CheckedElementCount(w, h);
			byte channelFlags = Reader.ReadByte();
			bool hasR = (channelFlags & 1) != 0;
			bool hasG = (channelFlags & 2) != 0;
			bool hasB = (channelFlags & 4) != 0;
			bool hasA = (channelFlags & 8) != 0;
			bool isMono = (channelFlags & 16) != 0;

			byte[] rPlane = hasR
				? ReadPaethChannelPlane(Reader, w, h, pixelCount, Background.R)
				: FillPlane(total, Reader.ReadByte());
			byte[] gPlane, bPlane;
			if (isMono)
			{
				gPlane = rPlane;
				bPlane = rPlane;
			}
			else
			{
				gPlane = hasG
					? ReadPaethChannelPlane(Reader, w, h, pixelCount, Background.G)
					: FillPlane(total, Reader.ReadByte());
				bPlane = hasB
					? ReadPaethChannelPlane(Reader, w, h, pixelCount, Background.B)
					: FillPlane(total, Reader.ReadByte());
			}
			byte[] aPlane = hasA
				? ReadPaethChannelPlane(Reader, w, h, pixelCount, Background.A)
				: FillPlane(total, Reader.ReadByte());

			// Extract stencil-true pixels from the reconstructed full grid
			ImageData = new Color[pixelCount];
			int idx = 0;
			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
					if (Descriptor.Get(x, y))
					{
						int i = y * w + x;
						ImageData[idx++] = Color.FromArgb(aPlane[i], rPlane[i], gPlane[i], bPlane[i]);
					}
		}

		// Decompress stencil-true residuals, rebuild full grid with background, then Paeth-decode
		byte[] ReadPaethChannelPlane(BinaryReader r, int width, int height, int pixelCount, byte bgVal)
		{
			int compLen = r.ReadInt32();
			byte[] compressed = ApfReadHelpers.ReadBytesExact(r, compLen, "Paeth channel plane");
			byte[] stencilResiduals = Helpers.Decompress(compressed, pixelCount);

			int total = ApfReadHelpers.CheckedElementCount(width, height);
			byte[] fullResiduals = new byte[total];

			// Insert stencil-true residuals at their grid positions;
			// stencil-false positions get the residual that would produce bgVal after Paeth decode.
			// We must compute these on the fly during Paeth decode instead.
			// Use a custom decode that knows the stencil and background.
			byte[] result = new byte[total];
			int si = 0;
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					int i = y * width + x;
					byte a = x > 0 ? result[i - 1] : (byte)0;
					byte b = y > 0 ? result[i - width] : (byte)0;
					byte c = (x > 0 && y > 0) ? result[i - width - 1] : (byte)0;
					byte predicted = Helpers.PaethPredict(a, b, c);

					if (Descriptor.Get(x, y))
						result[i] = (byte)(stencilResiduals[si++] + predicted);
					else
						result[i] = bgVal;
				}
			}

			return result;
		}

		// --- Deserialize ---

		void Deserialize(Stream S)
		{
			using (BinaryReader Reader = new BinaryReader(S, Encoding.UTF8, true))
			{
				byte version = Reader.ReadByte();
				if (version != FORMAT_VERSION)
					throw new InvalidDataException("Unknown APF format version: 0x" + version.ToString("X2"));

				DeserializePayload(Reader);
			}
		}

		public void DeserializePayload(BinaryReader Reader)
		{
			Descriptor = ShapeDesc.FromStream(Reader);
			int totalPixels = ApfReadHelpers.CheckedElementCount(Descriptor.Width, Descriptor.Height);
			int shapePixelCount = Descriptor.GetCount();
			Background = Color.FromArgb(Reader.ReadInt32());
			int pixelCount = Reader.ReadInt32();
			ApfReadHelpers.ValidateCount(pixelCount, "image pixel", totalPixels);
			if (pixelCount != shapePixelCount)
				throw new InvalidDataException("Image pixel count does not match the shape descriptor.");

			PixelEncoding mode = (PixelEncoding)Reader.ReadByte();

			switch (mode)
			{
				case PixelEncoding.ChannelPlanes: DecodeChannelPlanes(Reader, pixelCount); break;
				case PixelEncoding.PaletteIndexed: DecodePaletteIndexed(Reader, pixelCount); break;
				case PixelEncoding.ColorSorted: DecodeColorSorted(Reader, pixelCount); break;
				case PixelEncoding.SolidFill: DecodeSolidFill(Reader, pixelCount); break;
				case PixelEncoding.MonoAlpha: DecodeMonoAlpha(Reader, pixelCount); break;
				case PixelEncoding.PaethFullGrid: DecodePaethFullGrid(Reader, pixelCount); break;
				case PixelEncoding.PaethChannelPlanes: DecodePaethChannelPlanes(Reader, pixelCount); break;
				default: throw new InvalidDataException("Unknown pixel encoding mode: " + (int)mode);
			}
		}

		void DecodeChannelPlanes(BinaryReader Reader, int pixelCount)
		{
			byte flags = Reader.ReadByte();
			bool isMono = (flags & 1) != 0;
			bool hasR = (flags & 2) != 0;
			bool hasG = (flags & 4) != 0;
			bool hasB = (flags & 8) != 0;
			bool hasA = (flags & 16) != 0;

			byte defR = Reader.ReadByte();
			byte defG = Reader.ReadByte();
			byte defB = Reader.ReadByte();
			byte defA = Reader.ReadByte();

			byte[] rPlane, gPlane, bPlane, aPlane;

			if (isMono)
			{
				rPlane = hasR ? ReadCompressedPlane(Reader, pixelCount) : FillPlane(pixelCount, defR);
				gPlane = rPlane;
				bPlane = rPlane;
			}
			else
			{
				rPlane = hasR ? ReadCompressedPlane(Reader, pixelCount) : FillPlane(pixelCount, defR);
				gPlane = hasG ? ReadCompressedPlane(Reader, pixelCount) : FillPlane(pixelCount, defG);
				bPlane = hasB ? ReadCompressedPlane(Reader, pixelCount) : FillPlane(pixelCount, defB);
			}
			aPlane = hasA ? ReadCompressedPlane(Reader, pixelCount) : FillPlane(pixelCount, defA);

			Color[] zPixels = new Color[pixelCount];
			for (int i = 0; i < pixelCount; i++)
				zPixels[i] = Color.FromArgb(aPlane[i], rPlane[i], gPlane[i], bPlane[i]);

			int[] zOrder = Helpers.GenerateZOrderIndices(Descriptor.Width, Descriptor.Height);
			ReorderPixelsFromZOrder(zOrder, zPixels);
		}

		void DecodePaletteIndexed(BinaryReader Reader, int pixelCount)
		{
			int paletteCount = Reader.ReadUInt16();
			if (paletteCount <= 0 || paletteCount > 256)
				throw new InvalidDataException("Palette color count must be between 1 and 256.");

			Color[] palette = new Color[paletteCount];
			for (int i = 0; i < paletteCount; i++)
				palette[i] = Color.FromArgb(Reader.ReadInt32());

			byte bitsPerIndex = Reader.ReadByte();
			if (bitsPerIndex != 1 && bitsPerIndex != 2 && bitsPerIndex != 4 && bitsPerIndex != 8)
				throw new InvalidDataException("Invalid palette index bit width.");

			int packedLen = CheckedPackedLength(pixelCount, bitsPerIndex);

			int compLen = Reader.ReadInt32();
			byte[] compressed = ApfReadHelpers.ReadBytesExact(Reader, compLen, "palette index data");
			byte[] delta = Helpers.Decompress(compressed, packedLen);
			byte[] packed = Helpers.DeltaDecode(delta);
			byte[] indices = bitsPerIndex == 8 ? packed : Helpers.UnpackBits(packed, bitsPerIndex, pixelCount);

			Color[] zPixels = new Color[pixelCount];
			for (int i = 0; i < pixelCount; i++)
			{
				if (indices[i] >= paletteCount)
					throw new InvalidDataException("Palette index is outside the palette.");
				zPixels[i] = palette[indices[i]];
			}

			int[] zOrder = Helpers.GenerateZOrderIndices(Descriptor.Width, Descriptor.Height);
			ReorderPixelsFromZOrder(zOrder, zPixels);
		}

		void DecodeColorSorted(BinaryReader Reader, int pixelCount)
		{
			int uniqueCount = Reader.ReadInt32();
			ApfReadHelpers.ValidateCount(uniqueCount, "unique color", pixelCount);

			int[] colors = new int[uniqueCount];
			for (int i = 0; i < uniqueCount; i++)
				colors[i] = Reader.ReadInt32();

			int[] counts = new int[uniqueCount];
			long totalCount = 0;
			for (int i = 0; i < uniqueCount; i++)
			{
				counts[i] = Reader.ReadInt32();
				ApfReadHelpers.ValidateCount(counts[i], "color run", pixelCount);
				totalCount += counts[i];
			}
			if (totalCount != pixelCount)
				throw new InvalidDataException("Color-sorted counts do not match the image pixel count.");

			byte posWidth = Reader.ReadByte();
			if (posWidth != 1 && posWidth != 2 && posWidth != 4)
				throw new InvalidDataException("Invalid color-sorted position width.");

			int compLen = Reader.ReadInt32();
			byte[] compressed = ApfReadHelpers.ReadBytesExact(Reader, compLen, "color-sorted positions");
			byte[] posBytes = Helpers.Decompress(compressed, checked(pixelCount * posWidth));
			int[] positionDeltas = Helpers.BytesToInts(posBytes, posWidth);

			ImageData = new Color[pixelCount];
			int di = 0;
			for (int c = 0; c < uniqueCount; c++)
			{
				Color color = Color.FromArgb(colors[c]);
				int pos = 0;
				for (int j = 0; j < counts[c]; j++)
				{
					if (j == 0)
						pos = positionDeltas[di++];
					else
						pos += positionDeltas[di++];
					if (pos < 0 || pos >= pixelCount)
						throw new InvalidDataException("Color-sorted pixel position is outside the image.");
					ImageData[pos] = color;
				}
			}
		}

		void DecodeSolidFill(BinaryReader Reader, int pixelCount)
		{
			Color fillColor = Color.FromArgb(Reader.ReadInt32());
			ImageData = new Color[pixelCount];
			for (int i = 0; i < pixelCount; i++)
				ImageData[i] = fillColor;
		}

		void DecodeMonoAlpha(BinaryReader Reader, int pixelCount)
		{
			byte[] luma = ReadCompressedPlane(Reader, pixelCount);

			byte hasAlpha = Reader.ReadByte();
			byte[] alpha;
			if (hasAlpha != 0)
			{
				alpha = ReadCompressedPlane(Reader, pixelCount);
			}
			else
			{
				byte constAlpha = Reader.ReadByte();
				alpha = FillPlane(pixelCount, constAlpha);
			}

			Color[] zPixels = new Color[pixelCount];
			for (int i = 0; i < pixelCount; i++)
				zPixels[i] = Color.FromArgb(alpha[i], luma[i], luma[i], luma[i]);

			int[] zOrder = Helpers.GenerateZOrderIndices(Descriptor.Width, Descriptor.Height);
			ReorderPixelsFromZOrder(zOrder, zPixels);
		}

		void DecodePaethFullGrid(BinaryReader Reader, int pixelCount)
		{
			int w = Descriptor.Width, h = Descriptor.Height;
			int total = ApfReadHelpers.CheckedElementCount(w, h);
			byte channelFlags = Reader.ReadByte();
			bool hasR = (channelFlags & 1) != 0;
			bool hasG = (channelFlags & 2) != 0;
			bool hasB = (channelFlags & 4) != 0;
			bool hasA = (channelFlags & 8) != 0;
			bool isMono = (channelFlags & 16) != 0;

			byte[] rPlane = hasR ? ReadPaethPlane(Reader, w, h) : FillPlane(total, Reader.ReadByte());
			byte[] gPlane, bPlane;
			if (isMono)
			{
				gPlane = rPlane;
				bPlane = rPlane;
			}
			else
			{
				gPlane = hasG ? ReadPaethPlane(Reader, w, h) : FillPlane(total, Reader.ReadByte());
				bPlane = hasB ? ReadPaethPlane(Reader, w, h) : FillPlane(total, Reader.ReadByte());
			}
			byte[] aPlane = hasA ? ReadPaethPlane(Reader, w, h) : FillPlane(total, Reader.ReadByte());

			int count = Descriptor.GetCount();
			ImageData = new Color[count];
			int idx = 0;
			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
					if (Descriptor.Get(x, y))
					{
						int i = y * w + x;
						ImageData[idx++] = Color.FromArgb(aPlane[i], rPlane[i], gPlane[i], bPlane[i]);
					}
		}

		static byte[] FillPlane(int count, byte val)
		{
			ApfReadHelpers.ValidateLength(count, "plane");
			byte[] plane = new byte[count];
			for (int i = 0; i < count; i++) plane[i] = val;
			return plane;
		}

		static int CheckedPackedLength(int count, byte bitsPerIndex)
		{
			long totalBits = (long)count * bitsPerIndex;
			long packedLength = (totalBits + 7) / 8;
			if (packedLength > ApfReadHelpers.MaxByteArrayLength)
				throw new InvalidDataException("Packed index data is too large.");
			return (int)packedLength;
		}
	}

	public struct ShapeDesc
	{
		readonly struct StencilCandidate
		{
			public StencilCandidate(StencilEncodingMode mode, byte[] rawBits)
			{
				Mode = mode;
				RawBits = rawBits;
				Compressed = Helpers.Compress(rawBits);
			}

			public StencilEncodingMode Mode { get; }
			public byte[] RawBits { get; }
			public byte[] Compressed { get; }
		}

		public int Width, Height;
		public BitArray Data;

		public static ShapeDesc FromStream(Stream S)
		{
			ShapeDesc H = new ShapeDesc();
			H.Deserialize(S);
			return H;
		}

		public static ShapeDesc FromStream(BinaryReader Reader)
		{
			ShapeDesc H = new ShapeDesc();
			H.Deserialize(Reader);
			return H;
		}

		public ShapeDesc(int Width, int Height)
		{
			this.Width = Width;
			this.Height = Height;
			Data = new BitArray(Width * Height);
			Data.SetAll(false);
		}

		public int GetCount()
		{
			int C = 0;
			for (int i = 0; i < Data.Length; i++)
				C += Data[i] ? 1 : 0;
			return C;
		}

		public bool Get(int X, int Y)
		{
			return Data[Y * Width + X];
		}

		public void Set(int X, int Y, bool Val)
		{
			Data[Y * Width + X] = Val;
		}

		public void Serialize(Stream S)
		{
			using (BinaryWriter Writer = new BinaryWriter(S, Encoding.UTF8, true))
				Serialize(Writer);
		}

		public void Serialize(BinaryWriter Writer)
		{
			Writer.Write(Width);
			Writer.Write(Height);

			int total = Width * Height;
			if (GetCount() == total)
			{
				Writer.Write(0);
				Writer.Write(0);
				return;
			}

			StencilCandidate candidate = SelectBestStencilCandidate();
			if (candidate.Mode == StencilEncodingMode.ZOrder)
				Writer.Write(candidate.RawBits.Length);
			else
				Writer.Write(-GetStencilEncodingMarker(candidate.Mode));

			Writer.Write(candidate.Compressed.Length);
			Writer.Write(candidate.Compressed);
		}

		public StencilEncodingAnalysis AnalyzeEncoding()
		{
			int total = Width * Height;
			if (GetCount() == total)
				return new StencilEncodingAnalysis(StencilEncodingMode.FullCoverage, true, 0, 16);

			StencilCandidate candidate = SelectBestStencilCandidate();
			return new StencilEncodingAnalysis(
				candidate.Mode,
				false,
				candidate.RawBits.Length,
				16 + candidate.Compressed.Length,
				new CompressionAnalysis((CompressionMode)candidate.Compressed[0], candidate.RawBits.Length, candidate.Compressed.Length));
		}

		void Deserialize(Stream S)
		{
			using (BinaryReader Reader = new BinaryReader(S, Encoding.UTF8, true))
				Deserialize(Reader);
		}

		void Deserialize(BinaryReader Reader)
		{
			Width = Reader.ReadInt32();
			Height = Reader.ReadInt32();
			int total = ApfReadHelpers.CheckedElementCount(Width, Height);

			int rawLen = Reader.ReadInt32();
			int compLen = Reader.ReadInt32();
			ApfReadHelpers.ValidateLength(compLen, "compressed shape stencil");

			if (rawLen == 0 && compLen == 0)
			{
				Data = new BitArray(total);
				Data.SetAll(true);
				return;
			}

			int expectedRawLen = (total + 7) / 8;
			if (compLen == 0)
				throw new InvalidDataException("Compressed shape stencil cannot be empty.");

			StencilEncodingMode mode;
			if (rawLen > 0)
			{
				ApfReadHelpers.ValidateLength(rawLen, "shape stencil");
				if (rawLen != expectedRawLen)
					throw new InvalidDataException("Shape stencil length does not match image dimensions.");
				mode = StencilEncodingMode.ZOrder;
			}
			else
			{
				mode = GetStencilEncodingMode(-rawLen);
			}

			byte[] compressed = ApfReadHelpers.ReadBytesExact(Reader, compLen, "shape stencil");
			byte[] raw = Helpers.Decompress(compressed, expectedRawLen);
			TrimUnusedBits(raw, total);

			Data = new BitArray(total);
			switch (mode)
			{
				case StencilEncodingMode.ZOrder:
					{
						BitArray zData = raw.ToBitArray();
						int[] zOrder = Helpers.GenerateZOrderIndices(Width, Height);
						for (int i = 0; i < zOrder.Length; i++)
							Data[zOrder[i]] = zData[i];
						break;
					}
				case StencilEncodingMode.InvertedZOrder:
					{
						InvertBits(raw, total);
						BitArray zData = raw.ToBitArray();
						int[] zOrder = Helpers.GenerateZOrderIndices(Width, Height);
						for (int i = 0; i < zOrder.Length; i++)
							Data[zOrder[i]] = zData[i];
						break;
					}
				case StencilEncodingMode.Scanline:
					Data = raw.ToBitArray();
					break;
				case StencilEncodingMode.InvertedScanline:
					InvertBits(raw, total);
					Data = raw.ToBitArray();
					break;
				default:
					throw new InvalidDataException("Unknown shape stencil encoding mode.");
			}
		}

		StencilCandidate SelectBestStencilCandidate()
		{
			List<StencilCandidate> candidates = BuildStencilCandidates();
			StencilCandidate best = candidates[0];
			for (int i = 1; i < candidates.Count; i++)
			{
				if (candidates[i].Compressed.Length < best.Compressed.Length ||
					(candidates[i].Compressed.Length == best.Compressed.Length && candidates[i].Mode < best.Mode))
				{
					best = candidates[i];
				}
			}

			return best;
		}

		List<StencilCandidate> BuildStencilCandidates()
		{
			byte[] scanline = Data.ToByteArray();
			TrimUnusedBits(scanline, Width * Height);

			int[] zOrder = Helpers.GenerateZOrderIndices(Width, Height);
			BitArray zData = new BitArray(Data.Length);
			for (int i = 0; i < zOrder.Length; i++)
				zData[i] = Data[zOrder[i]];
			byte[] zOrderBytes = zData.ToByteArray();
			TrimUnusedBits(zOrderBytes, Width * Height);

			byte[] invertedScanline = (byte[])scanline.Clone();
			InvertBits(invertedScanline, Width * Height);
			byte[] invertedZOrder = (byte[])zOrderBytes.Clone();
			InvertBits(invertedZOrder, Width * Height);

			return new List<StencilCandidate>
			{
				new StencilCandidate(StencilEncodingMode.ZOrder, zOrderBytes),
				new StencilCandidate(StencilEncodingMode.InvertedZOrder, invertedZOrder),
				new StencilCandidate(StencilEncodingMode.Scanline, scanline),
				new StencilCandidate(StencilEncodingMode.InvertedScanline, invertedScanline),
			};
		}

		static int GetStencilEncodingMarker(StencilEncodingMode mode)
		{
			return mode switch
			{
				StencilEncodingMode.InvertedZOrder => 1,
				StencilEncodingMode.Scanline => 2,
				StencilEncodingMode.InvertedScanline => 3,
				_ => throw new InvalidOperationException("Legacy Z-order and full-coverage stencils do not use explicit markers."),
			};
		}

		static StencilEncodingMode GetStencilEncodingMode(int marker)
		{
			return marker switch
			{
				1 => StencilEncodingMode.InvertedZOrder,
				2 => StencilEncodingMode.Scanline,
				3 => StencilEncodingMode.InvertedScanline,
				_ => throw new InvalidDataException("Unknown shape stencil encoding mode."),
			};
		}

		static void InvertBits(byte[] raw, int totalBits)
		{
			for (int i = 0; i < raw.Length; i++)
				raw[i] = (byte)~raw[i];
			TrimUnusedBits(raw, totalBits);
		}

		static void TrimUnusedBits(byte[] raw, int totalBits)
		{
			int usedBits = totalBits % 8;
			if (raw.Length == 0 || usedBits == 0)
				return;

			raw[raw.Length - 1] &= (byte)((1 << usedBits) - 1);
		}
	}

	public static class Helpers
	{
		public static byte[] ToByteArray(this BitArray BA)
		{
			byte[] Arr = new byte[(int)Math.Ceiling((double)BA.Length / (sizeof(byte) * 8))];
			BA.CopyTo(Arr, 0);
			return Arr;
		}

		public static BitArray ToBitArray(this byte[] Vals)
		{
			return new BitArray(Vals);
		}

		public static bool IsHomogenous(this byte[] Bytes)
		{
			byte B = Bytes[0];
			for (int i = 0; i < Bytes.Length; i++)
				if (Bytes[i] != B)
					return false;
			return true;
		}

#if WINDOWS7_0_OR_GREATER
		public static BitmapData LockBits(this Bitmap Bmp)
		{
			return Bmp.LockBits(new Rectangle(0, 0, Bmp.Width, Bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
		}

		public static Color GetPixel(this BitmapData Data, int X, int Y)
		{
			return Color.FromArgb(Marshal.ReadInt32(Data.Scan0, Y * Data.Stride + X * 4));
		}

		public static int GetPixelArgb(this BitmapData Data, int X, int Y)
		{
			return Marshal.ReadInt32(Data.Scan0, Y * Data.Stride + X * 4);
		}

		public static void SetPixel(this BitmapData Data, int X, int Y, Color Clr)
		{
			Marshal.WriteInt32(Data.Scan0, Y * Data.Stride + X * 4, Clr.ToArgb());
		}
#endif

		// --- RLE ---

		public static byte[] RleEncode(byte[] data)
		{
			if (data.Length == 0) return new byte[0];

			List<byte> result = new List<byte>();
			int i = 0;

			while (i < data.Length)
			{
				int runLen = 1;
				while (i + runLen < data.Length && data[i + runLen] == data[i] && runLen < 129)
					runLen++;

				if (runLen >= 2)
				{
					result.Add((byte)(0x80 | (runLen - 2)));
					result.Add(data[i]);
					i += runLen;
				}
				else
				{
					int litStart = i;
					int litLen = 0;

					while (i < data.Length && litLen < 128)
					{
						if (i + 1 < data.Length && data[i] == data[i + 1])
							break;
						litLen++;
						i++;
					}

					if (litLen == 0)
					{
						litLen = 1;
						i++;
					}

					result.Add((byte)(litLen - 1));
					for (int j = litStart; j < litStart + litLen; j++)
						result.Add(data[j]);
				}
			}

			return result.ToArray();
		}

		public static byte[] RleDecode(byte[] data, int decodedLength)
		{
			ValidateDecodedLength(decodedLength);
			byte[] result = new byte[decodedLength];
			int ri = 0, di = 0;

			while (di < data.Length && ri < decodedLength)
			{
				byte header = data[di++];

				if ((header & 0x80) != 0)
				{
					int count = (header & 0x7F) + 2;
					if (di >= data.Length)
						throw new InvalidDataException("Truncated RLE run.");
					byte val = data[di++];
					for (int j = 0; j < count && ri < decodedLength; j++)
						result[ri++] = val;
				}
				else
				{
					int count = (header & 0x7F) + 1;
					if (data.Length - di < count)
						throw new InvalidDataException("Truncated RLE literal run.");
					for (int j = 0; j < count && ri < decodedLength; j++)
						result[ri++] = data[di++];
				}
			}

			if (ri != decodedLength)
				throw new InvalidDataException("RLE stream ended before the expected decoded length.");
			if (di != data.Length)
				throw new InvalidDataException("RLE stream has trailing data.");

			return result;
		}

		// --- Delta ---

		public static byte[] DeltaEncode(byte[] data)
		{
			if (data.Length == 0) return new byte[0];
			byte[] result = new byte[data.Length];
			result[0] = data[0];
			for (int i = 1; i < data.Length; i++)
				result[i] = (byte)(data[i] - data[i - 1]);
			return result;
		}

		public static byte[] DeltaDecode(byte[] data)
		{
			if (data.Length == 0) return new byte[0];
			byte[] result = new byte[data.Length];
			result[0] = data[0];
			for (int i = 1; i < data.Length; i++)
				result[i] = (byte)(data[i] + result[i - 1]);
			return result;
		}

		// --- Bit packing ---

		public static byte[] PackBits(byte[] values, byte bitsPerValue)
		{
			int totalBits = values.Length * bitsPerValue;
			byte[] packed = new byte[(totalBits + 7) / 8];
			int bitPos = 0;

			for (int i = 0; i < values.Length; i++)
			{
				int byteIdx = bitPos / 8;
				int bitOffset = bitPos % 8;
				packed[byteIdx] |= (byte)(values[i] << bitOffset);
				if (bitOffset + bitsPerValue > 8)
					packed[byteIdx + 1] |= (byte)(values[i] >> (8 - bitOffset));
				bitPos += bitsPerValue;
			}

			return packed;
		}

		public static byte[] UnpackBits(byte[] packed, byte bitsPerValue, int count)
		{
			byte[] result = new byte[count];
			byte mask = (byte)((1 << bitsPerValue) - 1);
			int bitPos = 0;

			for (int i = 0; i < count; i++)
			{
				int byteIdx = bitPos / 8;
				int bitOffset = bitPos % 8;
				int val = packed[byteIdx] >> bitOffset;
				if (bitOffset + bitsPerValue > 8 && byteIdx + 1 < packed.Length)
					val |= packed[byteIdx + 1] << (8 - bitOffset);
				result[i] = (byte)(val & mask);
				bitPos += bitsPerValue;
			}

			return result;
		}

		// --- Paeth prediction ---

		public static byte PaethPredict(byte a, byte b, byte c)
		{
			int p = a + b - c;
			int pa = Math.Abs(p - a);
			int pb = Math.Abs(p - b);
			int pc = Math.Abs(p - c);
			if (pa <= pb && pa <= pc) return a;
			if (pb <= pc) return b;
			return c;
		}

		public static byte[] PaethEncode(byte[] plane, int width, int height)
		{
			byte[] result = new byte[plane.Length];
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					int i = y * width + x;
					byte a = x > 0 ? plane[i - 1] : (byte)0;
					byte b = y > 0 ? plane[i - width] : (byte)0;
					byte c = (x > 0 && y > 0) ? plane[i - width - 1] : (byte)0;
					result[i] = (byte)(plane[i] - PaethPredict(a, b, c));
				}
			}
			return result;
		}

		public static byte[] PaethDecode(byte[] residuals, int width, int height)
		{
			byte[] result = new byte[residuals.Length];
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					int i = y * width + x;
					byte a = x > 0 ? result[i - 1] : (byte)0;
					byte b = y > 0 ? result[i - width] : (byte)0;
					byte c = (x > 0 && y > 0) ? result[i - width - 1] : (byte)0;
					result[i] = (byte)(residuals[i] + PaethPredict(a, b, c));
				}
			}
			return result;
		}

		// --- Morton / Z-order ---

		static uint MortonSpread(uint x)
		{
			x = (x | (x << 8)) & 0x00FF00FF;
			x = (x | (x << 4)) & 0x0F0F0F0F;
			x = (x | (x << 2)) & 0x33333333;
			x = (x | (x << 1)) & 0x55555555;
			return x;
		}

		public static uint MortonEncode(uint x, uint y)
		{
			return MortonSpread(x) | (MortonSpread(y) << 1);
		}

		public static int[] GenerateZOrderIndices(int width, int height)
		{
			int total = width * height;
			int[] indices = new int[total];
			uint[] mortonCodes = new uint[total];

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					int scanIdx = y * width + x;
					indices[scanIdx] = scanIdx;
					mortonCodes[scanIdx] = MortonEncode((uint)x, (uint)y);
				}
			}

			Array.Sort(mortonCodes, indices);
			return indices;
		}

		// --- Variable-width int encoding ---

		public static byte[] IntsToBytes(int[] values, byte width)
		{
			byte[] result = new byte[values.Length * width];
			for (int i = 0; i < values.Length; i++)
			{
				int v = values[i];
				int off = i * width;
				if (width == 1)
				{
					result[off] = (byte)v;
				}
				else if (width == 2)
				{
					result[off] = (byte)v;
					result[off + 1] = (byte)(v >> 8);
				}
				else
				{
					result[off] = (byte)v;
					result[off + 1] = (byte)(v >> 8);
					result[off + 2] = (byte)(v >> 16);
					result[off + 3] = (byte)(v >> 24);
				}
			}
			return result;
		}

		public static int[] BytesToInts(byte[] data, byte width)
		{
			int count = data.Length / width;
			int[] result = new int[count];
			for (int i = 0; i < count; i++)
			{
				int off = i * width;
				if (width == 1)
				{
					result[i] = data[off];
				}
				else if (width == 2)
				{
					result[i] = data[off] | (data[off + 1] << 8);
				}
				else
				{
					result[i] = data[off] | (data[off + 1] << 8) | (data[off + 2] << 16) | (data[off + 3] << 24);
				}
			}
			return result;
		}

		// --- LZ77 sliding-window compression ---

		const int LZ_WINDOW_SIZE = 32768;
		const int LZ_MIN_MATCH = 3;
		const int LZ_MAX_MATCH = 130;
		const int LZ_MAX_LITERAL = 128;
		const int LZ_HASH_SIZE = 65536;

		public static byte[] Lz77Encode(byte[] data)
		{
			if (data.Length == 0) return new byte[0];

			var result = new List<byte>();
			int[] head = new int[LZ_HASH_SIZE];
			int[] prev = new int[data.Length];
			for (int i = 0; i < head.Length; i++) head[i] = -1;

			int pos = 0;
			var literals = new List<byte>();

			while (pos < data.Length)
			{
				int bestLen = 0, bestDist = 0;

				if (pos + LZ_MIN_MATCH <= data.Length)
				{
					int h = LzHash(data, pos);
					int chainLen = 0;
					int maxChain = 256;
					int candidate = head[h];

					while (candidate >= 0 && pos - candidate <= LZ_WINDOW_SIZE && chainLen < maxChain)
					{
						int matchLen = 0;
						int maxLen = Math.Min(LZ_MAX_MATCH, data.Length - pos);
						while (matchLen < maxLen && data[candidate + matchLen] == data[pos + matchLen])
							matchLen++;

						if (matchLen >= LZ_MIN_MATCH && matchLen > bestLen)
						{
							bestLen = matchLen;
							bestDist = pos - candidate;
							if (bestLen == LZ_MAX_MATCH) break;
						}
						candidate = prev[candidate];
						chainLen++;
					}

					prev[pos] = head[h];
					head[h] = pos;
				}

				if (bestLen >= LZ_MIN_MATCH)
				{
					FlushLiterals(result, literals);
					result.Add((byte)(0x80 | (bestLen - LZ_MIN_MATCH)));
					result.Add((byte)(bestDist & 0xFF));
					result.Add((byte)((bestDist >> 8) & 0xFF));

					for (int j = 1; j < bestLen && pos + j + 2 < data.Length; j++)
					{
						int hj = LzHash(data, pos + j);
						prev[pos + j] = head[hj];
						head[hj] = pos + j;
					}
					pos += bestLen;
				}
				else
				{
					literals.Add(data[pos]);
					if (literals.Count == LZ_MAX_LITERAL)
						FlushLiterals(result, literals);
					pos++;
				}
			}

			FlushLiterals(result, literals);
			return result.ToArray();
		}

		static int LzHash(byte[] data, int pos)
		{
			return ((data[pos] << 8) ^ (data[pos + 1] << 4) ^ data[pos + 2]) & (LZ_HASH_SIZE - 1);
		}

		static void FlushLiterals(List<byte> result, List<byte> literals)
		{
			if (literals.Count == 0) return;
			result.Add((byte)(literals.Count - 1));
			result.AddRange(literals);
			literals.Clear();
		}

		public static byte[] Lz77Decode(byte[] data, int decodedLength)
		{
			ValidateDecodedLength(decodedLength);
			byte[] result = new byte[decodedLength];
			int ri = 0, di = 0;

			while (di < data.Length && ri < decodedLength)
			{
				byte header = data[di++];

				if ((header & 0x80) != 0)
				{
					int len = (header & 0x7F) + LZ_MIN_MATCH;
					if (data.Length - di < 2)
						throw new InvalidDataException("Truncated LZ77 match.");
					int dist = data[di] | (data[di + 1] << 8);
					di += 2;
					if (dist <= 0 || dist > ri)
						throw new InvalidDataException("Invalid LZ77 match distance.");
					int srcPos = ri - dist;
					for (int j = 0; j < len && ri < decodedLength; j++)
						result[ri++] = result[srcPos + j];
				}
				else
				{
					int len = (header & 0x7F) + 1;
					if (data.Length - di < len)
						throw new InvalidDataException("Truncated LZ77 literal run.");
					for (int j = 0; j < len && ri < decodedLength; j++)
						result[ri++] = data[di++];
				}
			}

			if (ri != decodedLength)
				throw new InvalidDataException("LZ77 stream ended before the expected decoded length.");
			if (di != data.Length)
				throw new InvalidDataException("LZ77 stream has trailing data.");

			return result;
		}

		// --- rANS (Range Asymmetric Numeral Systems) entropy coding ---

		const int RANS_SCALE_BITS = 12;
		const int RANS_SCALE = 1 << RANS_SCALE_BITS; // 4096
		const uint RANS_LOWER = 1u << 23;

		static int[] RansNormalizeFrequencies(int[] freq, int total)
		{
			int[] norm = new int[256];
			int assigned = 0;

			for (int i = 0; i < 256; i++)
			{
				if (freq[i] > 0)
				{
					norm[i] = Math.Max(1, (int)Math.Round((double)freq[i] * RANS_SCALE / total));
					assigned += norm[i];
				}
			}

			int diff = assigned - RANS_SCALE;
			while (diff != 0)
			{
				int bestIdx = -1;
				double bestScore = double.NegativeInfinity;

				for (int i = 0; i < 256; i++)
				{
					if (norm[i] == 0) continue;
					if (diff > 0 && norm[i] <= 1) continue;

					double ideal = (double)freq[i] * RANS_SCALE / total;
					double score = (diff > 0) ? (norm[i] - ideal) : (ideal - norm[i]);

					if (score > bestScore)
					{
						bestScore = score;
						bestIdx = i;
					}
				}

				if (bestIdx < 0) break;

				if (diff > 0) { norm[bestIdx]--; diff--; }
				else { norm[bestIdx]++; diff++; }
			}

			return norm;
		}

		public static byte[] RansEncode(byte[] data)
		{
			if (data.Length == 0) return new byte[0];

			// Count frequencies
			int[] freq = new int[256];
			for (int i = 0; i < data.Length; i++)
				freq[data[i]]++;

			int[] normFreq = RansNormalizeFrequencies(freq, data.Length);

			// Build cumulative frequency table
			int[] cumFreq = new int[257];
			for (int i = 0; i < 256; i++)
				cumFreq[i + 1] = cumFreq[i] + normFreq[i];

			// Encode symbols in reverse order
			uint state = RANS_LOWER;
			var encoded = new List<byte>();

			for (int i = data.Length - 1; i >= 0; i--)
			{
				byte s = data[i];
				int fs = normFreq[s];
				int cs = cumFreq[s];

				// Renormalize: stream out bytes until state is small enough
				uint xMax = ((RANS_LOWER >> RANS_SCALE_BITS) << 8) * (uint)fs;
				while (state >= xMax)
				{
					encoded.Add((byte)(state & 0xFF));
					state >>= 8;
				}

				// Encode: state = (state / fs) * M + (state % fs) + cs
				state = ((state / (uint)fs) << RANS_SCALE_BITS) + (state % (uint)fs) + (uint)cs;
			}

			// Build output: [freq_header][final_state][encoded_bytes_reversed]
			var result = new List<byte>();

			// Compact frequency table: [uint16 count][count × (byte sym, uint16 freq)]
			int nonZero = 0;
			for (int i = 0; i < 256; i++)
				if (normFreq[i] > 0) nonZero++;

			result.Add((byte)(nonZero & 0xFF));
			result.Add((byte)((nonZero >> 8) & 0xFF));

			for (int i = 0; i < 256; i++)
			{
				if (normFreq[i] > 0)
				{
					result.Add((byte)i);
					result.Add((byte)(normFreq[i] & 0xFF));
					result.Add((byte)((normFreq[i] >> 8) & 0xFF));
				}
			}

			// Final state (4 bytes LE)
			result.Add((byte)(state & 0xFF));
			result.Add((byte)((state >> 8) & 0xFF));
			result.Add((byte)((state >> 16) & 0xFF));
			result.Add((byte)((state >> 24) & 0xFF));

			// Encoded data in forward reading order
			for (int i = encoded.Count - 1; i >= 0; i--)
				result.Add(encoded[i]);

			return result.ToArray();
		}

		public static byte[] RansDecode(byte[] data, int decodedLength)
		{
			ValidateDecodedLength(decodedLength);
			if (decodedLength == 0)
				return new byte[0];
			if (data.Length < 6)
				throw new InvalidDataException("Truncated rANS stream.");

			int pos = 0;

			// Read frequency table
			int numSymbols = data[pos] | (data[pos + 1] << 8);
			pos += 2;
			if (numSymbols <= 0 || numSymbols > 256)
				throw new InvalidDataException("Invalid rANS symbol count.");
			if (data.Length - pos < numSymbols * 3 + 4)
				throw new InvalidDataException("Truncated rANS frequency table.");

			int[] freq = new int[256];
			int[] cumFreq = new int[257];
			int totalFrequency = 0;

			for (int i = 0; i < numSymbols; i++)
			{
				byte sym = data[pos++];
				int f = data[pos] | (data[pos + 1] << 8);
				pos += 2;
				if (f <= 0)
					throw new InvalidDataException("Invalid rANS symbol frequency.");
				if (freq[sym] != 0)
					throw new InvalidDataException("Duplicate rANS symbol frequency.");
				freq[sym] = f;
				totalFrequency += f;
			}
			if (totalFrequency != RANS_SCALE)
				throw new InvalidDataException("rANS frequencies do not match the coding scale.");

			for (int i = 0; i < 256; i++)
				cumFreq[i + 1] = cumFreq[i] + freq[i];

			// Reverse lookup table: cumulative freq → symbol
			byte[] cumToSym = new byte[RANS_SCALE];
			for (int s = 0; s < 256; s++)
				for (int j = cumFreq[s]; j < cumFreq[s + 1]; j++)
					cumToSym[j] = (byte)s;

			// Read initial state
			uint state = (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24));
			pos += 4;

			// Decode symbols
			byte[] result = new byte[decodedLength];

			for (int i = 0; i < decodedLength; i++)
			{
				uint cumVal = state & (uint)(RANS_SCALE - 1);
				byte s = cumToSym[cumVal];
				if (freq[s] == 0)
					throw new InvalidDataException("Invalid rANS cumulative frequency.");
				result[i] = s;

				// Advance state: state = freq[s] * (state >> SCALE) + (cumVal - cumFreq[s])
				state = (uint)freq[s] * (state >> RANS_SCALE_BITS) + cumVal - (uint)cumFreq[s];

				// Renormalize
				while (state < RANS_LOWER && pos < data.Length)
					state = (state << 8) | data[pos++];
				if (state < RANS_LOWER && pos >= data.Length && i + 1 < decodedLength)
					throw new InvalidDataException("Truncated rANS payload.");
			}

			return result;
		}

		// --- Compress/Decompress: picks best of RLE, LZ77, rANS ---
		// Prefix byte: 0 = RLE, 1 = LZ77, 2 = rANS, 3 = LZ77+rANS

		public static byte[] Compress(byte[] data)
		{
			byte[] rle = RleEncode(data);
			byte[] lz = Lz77Encode(data);
			byte[] rans = RansEncode(data);

			// LZ77+rANS: prefix LZ length so decoder knows intermediate size
			byte[] lzRansInner = RansEncode(lz);
			byte[] lzRans = new byte[4 + lzRansInner.Length];
			lzRans[0] = (byte)(lz.Length & 0xFF);
			lzRans[1] = (byte)((lz.Length >> 8) & 0xFF);
			lzRans[2] = (byte)((lz.Length >> 16) & 0xFF);
			lzRans[3] = (byte)((lz.Length >> 24) & 0xFF);
			Buffer.BlockCopy(lzRansInner, 0, lzRans, 4, lzRansInner.Length);

			// Pick the smallest: prefix + payload
			int rleTotal = 1 + rle.Length;
			int lzTotal = 1 + lz.Length;
			int ransTotal = 1 + rans.Length;
			int lzRansTotal = 1 + lzRans.Length;

			int best = rleTotal;
			byte bestMode = 0;

			if (lzTotal < best) { best = lzTotal; bestMode = 1; }
			if (ransTotal < best) { best = ransTotal; bestMode = 2; }
			if (lzRansTotal < best) { best = lzRansTotal; bestMode = 3; }

			byte[] payload;
			switch (bestMode)
			{
				case 0: payload = rle; break;
				case 1: payload = lz; break;
				case 2: payload = rans; break;
				default: payload = lzRans; break;
			}

			byte[] result = new byte[1 + payload.Length];
			result[0] = bestMode;
			Buffer.BlockCopy(payload, 0, result, 1, payload.Length);
			return result;
		}

		public static byte[] Decompress(byte[] data, int decodedLength)
		{
			ValidateDecodedLength(decodedLength);
			if (data.Length == 0)
			{
				if (decodedLength == 0)
					return new byte[0];
				throw new InvalidDataException("Compressed stream is empty.");
			}
			byte mode = data[0];
			byte[] payload = new byte[data.Length - 1];
			Buffer.BlockCopy(data, 1, payload, 0, payload.Length);

			switch (mode)
			{
				case 0: return RleDecode(payload, decodedLength);
				case 1: return Lz77Decode(payload, decodedLength);
				case 2: return RansDecode(payload, decodedLength);
				case 3:
					if (payload.Length < 4)
						throw new InvalidDataException("Truncated LZ77+rANS stream.");
					int lzLen = payload[0] | (payload[1] << 8) | (payload[2] << 16) | (payload[3] << 24);
					ValidateDecodedLength(lzLen);
					byte[] ransPayload = new byte[payload.Length - 4];
					Buffer.BlockCopy(payload, 4, ransPayload, 0, ransPayload.Length);
					byte[] lz = RansDecode(ransPayload, lzLen);
					return Lz77Decode(lz, decodedLength);
				default:
					throw new InvalidDataException("Unknown compression mode: " + mode);
			}
		}

		static void ValidateDecodedLength(int decodedLength)
		{
			ApfReadHelpers.ValidateLength(decodedLength, "decoded stream");
		}
	}
}
