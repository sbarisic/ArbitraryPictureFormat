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
	}

	public class ArbitraryPicture
	{
		public Color Background;
		public ShapeDesc Descriptor;
		public Color[] ImageData;

		public static ArbitraryPicture FromFile(string FilePath)
		{
			using (FileStream FS = File.OpenRead(FilePath))
				return new ArbitraryPicture(FS);
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
			Descriptor = new ShapeDesc(Img.Width, Img.Height);

			using (Bitmap Bmp = new Bitmap(Img))
			{
				BitmapData BmpData = Bmp.LockBits();

				// Find most common color to use as background
				Dictionary<int, int> colorCounts = new Dictionary<int, int>();
				for (int y = 0; y < Img.Height; y++)
				{
					for (int x = 0; x < Img.Width; x++)
					{
						int argb = BmpData.GetPixelArgb(x, y);
						if (colorCounts.ContainsKey(argb))
							colorCounts[argb]++;
						else
							colorCounts[argb] = 1;
					}
				}

				int bestArgb = 0;
				int bestCount = 0;
				foreach (var kvp in colorCounts)
				{
					if (kvp.Value > bestCount)
					{
						bestCount = kvp.Value;
						bestArgb = kvp.Key;
					}
				}
				Background = Color.FromArgb(bestArgb);

				for (int y = 0; y < Img.Height; y++)
				{
					for (int x = 0; x < Img.Width; x++)
					{
						Color C = BmpData.GetPixel(x, y);
						Descriptor.Set(x, y, C != Background);
					}
				}

				ImageData = new Color[Descriptor.GetCount()];

				int Idx = 0;
				for (int y = 0; y < Img.Height; y++)
				{
					for (int x = 0; x < Img.Width; x++)
					{
						if (Descriptor.Get(x, y))
							ImageData[Idx++] = BmpData.GetPixel(x, y);
					}
				}

				Bmp.UnlockBits(BmpData);
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

			Descriptor = new ShapeDesc(width, height);

			// Find most common color to use as background
			Dictionary<int, int> colorCounts = new Dictionary<int, int>();
			for (int i = 0; i < pixels.Length; i++)
			{
				int argb = pixels[i].ToArgb();
				if (colorCounts.ContainsKey(argb))
					colorCounts[argb]++;
				else
					colorCounts[argb] = 1;
			}

			int bestArgb = 0;
			int bestCount = 0;
			foreach (var kvp in colorCounts)
			{
				if (kvp.Value > bestCount)
				{
					bestCount = kvp.Value;
					bestArgb = kvp.Key;
				}
			}
			Background = Color.FromArgb(bestArgb);

			for (int y = 0; y < height; y++)
				for (int x = 0; x < width; x++)
				{
					Color c = pixels[y * width + x];
					Descriptor.Set(x, y, c != Background);
				}

			ImageData = new Color[Descriptor.GetCount()];

			int idx = 0;
			for (int y = 0; y < height; y++)
				for (int x = 0; x < width; x++)
					if (Descriptor.Get(x, y))
						ImageData[idx++] = pixels[y * width + x];
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

			var candidates = new List<(PixelEncoding mode, byte[] data)>();

			if (forcedEncoding == null)
			{
				candidates.Add((PixelEncoding.ChannelPlanes, EncodeChannelPlanes(zPixels)));

				if (ImageData.Length == 0 || IsSolidFill())
					candidates.Add((PixelEncoding.SolidFill, EncodeSolidFill()));

				var uniqueArgbs = GetUniqueArgbSet();
				if (uniqueArgbs.Count <= 256 && uniqueArgbs.Count > 0)
					candidates.Add((PixelEncoding.PaletteIndexed, EncodePaletteIndexed(zPixels, uniqueArgbs)));

				if (IsMonochrome())
					candidates.Add((PixelEncoding.MonoAlpha, EncodeMonoAlpha(zPixels)));

				if (ImageData.Length > 0)
					candidates.Add((PixelEncoding.ColorSorted, EncodeColorSorted()));

				candidates.Add((PixelEncoding.PaethFullGrid, EncodePaethFullGrid()));
			}
			else
			{
				// Try the forced encoding; fall back to auto-select if not applicable
				bool added = false;
				switch (forcedEncoding.Value)
				{
					case PixelEncoding.ChannelPlanes:
						candidates.Add((PixelEncoding.ChannelPlanes, EncodeChannelPlanes(zPixels)));
						added = true;
						break;
					case PixelEncoding.SolidFill:
						if (ImageData.Length == 0 || IsSolidFill())
						{
							candidates.Add((PixelEncoding.SolidFill, EncodeSolidFill()));
							added = true;
						}
						break;
					case PixelEncoding.PaletteIndexed:
						var ua = GetUniqueArgbSet();
						if (ua.Count <= 256 && ua.Count > 0)
						{
							candidates.Add((PixelEncoding.PaletteIndexed, EncodePaletteIndexed(zPixels, ua)));
							added = true;
						}
						break;
					case PixelEncoding.MonoAlpha:
						if (IsMonochrome())
						{
							candidates.Add((PixelEncoding.MonoAlpha, EncodeMonoAlpha(zPixels)));
							added = true;
						}
						break;
					case PixelEncoding.ColorSorted:
						if (ImageData.Length > 0)
						{
							candidates.Add((PixelEncoding.ColorSorted, EncodeColorSorted()));
							added = true;
						}
						break;
					case PixelEncoding.PaethFullGrid:
						candidates.Add((PixelEncoding.PaethFullGrid, EncodePaethFullGrid()));
						added = true;
						break;
				}

				if (!added)
				{
					// Forced encoding not applicable, fall back to auto
					candidates.Add((PixelEncoding.ChannelPlanes, EncodeChannelPlanes(zPixels)));
					if (ImageData.Length == 0 || IsSolidFill())
						candidates.Add((PixelEncoding.SolidFill, EncodeSolidFill()));
					var uniqueArgbs = GetUniqueArgbSet();
					if (uniqueArgbs.Count <= 256 && uniqueArgbs.Count > 0)
						candidates.Add((PixelEncoding.PaletteIndexed, EncodePaletteIndexed(zPixels, uniqueArgbs)));
					if (IsMonochrome())
						candidates.Add((PixelEncoding.MonoAlpha, EncodeMonoAlpha(zPixels)));
					if (ImageData.Length > 0)
						candidates.Add((PixelEncoding.ColorSorted, EncodeColorSorted()));
					candidates.Add((PixelEncoding.PaethFullGrid, EncodePaethFullGrid()));
				}
			}

			PixelEncoding bestMode = candidates[0].mode;
			byte[] bestData = candidates[0].data;
			for (int i = 1; i < candidates.Count; i++)
			{
				if (candidates[i].data.Length < bestData.Length)
				{
					bestMode = candidates[i].mode;
					bestData = candidates[i].data;
				}
			}

			Writer.Write((byte)bestMode);
			Writer.Write(bestData);
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
			byte[] compressed = r.ReadBytes(len);
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
			byte[] compressed = r.ReadBytes(len);
			byte[] residuals = Helpers.Decompress(compressed, width * height);
			return Helpers.PaethDecode(residuals, width, height);
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
			Background = Color.FromArgb(Reader.ReadInt32());
			int pixelCount = Reader.ReadInt32();
			PixelEncoding mode = (PixelEncoding)Reader.ReadByte();

			switch (mode)
			{
				case PixelEncoding.ChannelPlanes: DecodeChannelPlanes(Reader, pixelCount); break;
				case PixelEncoding.PaletteIndexed: DecodePaletteIndexed(Reader, pixelCount); break;
				case PixelEncoding.ColorSorted: DecodeColorSorted(Reader, pixelCount); break;
				case PixelEncoding.SolidFill: DecodeSolidFill(Reader, pixelCount); break;
				case PixelEncoding.MonoAlpha: DecodeMonoAlpha(Reader, pixelCount); break;
				case PixelEncoding.PaethFullGrid: DecodePaethFullGrid(Reader, pixelCount); break;
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
			Color[] palette = new Color[paletteCount];
			for (int i = 0; i < paletteCount; i++)
				palette[i] = Color.FromArgb(Reader.ReadInt32());

			byte bitsPerIndex = Reader.ReadByte();
			int packedLen = bitsPerIndex == 8 ? pixelCount : (pixelCount * bitsPerIndex + 7) / 8;

			int compLen = Reader.ReadInt32();
			byte[] compressed = Reader.ReadBytes(compLen);
			byte[] delta = Helpers.Decompress(compressed, packedLen);
			byte[] packed = Helpers.DeltaDecode(delta);
			byte[] indices = bitsPerIndex == 8 ? packed : Helpers.UnpackBits(packed, bitsPerIndex, pixelCount);

			Color[] zPixels = new Color[pixelCount];
			for (int i = 0; i < pixelCount; i++)
				zPixels[i] = palette[indices[i]];

			int[] zOrder = Helpers.GenerateZOrderIndices(Descriptor.Width, Descriptor.Height);
			ReorderPixelsFromZOrder(zOrder, zPixels);
		}

		void DecodeColorSorted(BinaryReader Reader, int pixelCount)
		{
			int uniqueCount = Reader.ReadInt32();

			int[] colors = new int[uniqueCount];
			for (int i = 0; i < uniqueCount; i++)
				colors[i] = Reader.ReadInt32();

			int[] counts = new int[uniqueCount];
			for (int i = 0; i < uniqueCount; i++)
				counts[i] = Reader.ReadInt32();

			byte posWidth = Reader.ReadByte();
			int compLen = Reader.ReadInt32();
			byte[] compressed = Reader.ReadBytes(compLen);
			byte[] posBytes = Helpers.Decompress(compressed, pixelCount * posWidth);
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
			byte channelFlags = Reader.ReadByte();
			bool hasR = (channelFlags & 1) != 0;
			bool hasG = (channelFlags & 2) != 0;
			bool hasB = (channelFlags & 4) != 0;
			bool hasA = (channelFlags & 8) != 0;
			bool isMono = (channelFlags & 16) != 0;

			byte[] rPlane = hasR ? ReadPaethPlane(Reader, w, h) : FillPlane(w * h, Reader.ReadByte());
			byte[] gPlane, bPlane;
			if (isMono)
			{
				gPlane = rPlane;
				bPlane = rPlane;
			}
			else
			{
				gPlane = hasG ? ReadPaethPlane(Reader, w, h) : FillPlane(w * h, Reader.ReadByte());
				bPlane = hasB ? ReadPaethPlane(Reader, w, h) : FillPlane(w * h, Reader.ReadByte());
			}
			byte[] aPlane = hasA ? ReadPaethPlane(Reader, w, h) : FillPlane(w * h, Reader.ReadByte());

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
			byte[] plane = new byte[count];
			for (int i = 0; i < count; i++) plane[i] = val;
			return plane;
		}
	}

	public struct ShapeDesc
	{
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

			int[] zOrder = Helpers.GenerateZOrderIndices(Width, Height);
			BitArray zData = new BitArray(Data.Length);
			for (int i = 0; i < zOrder.Length; i++)
				zData[i] = Data[zOrder[i]];

			byte[] raw = zData.ToByteArray();
			byte[] compressed = Helpers.Compress(raw);

			Writer.Write(raw.Length);
			Writer.Write(compressed.Length);
			Writer.Write(compressed);
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

			int rawLen = Reader.ReadInt32();
			int compLen = Reader.ReadInt32();

			if (rawLen == 0 && compLen == 0)
			{
				Data = new BitArray(Width * Height);
				Data.SetAll(true);
				return;
			}

			byte[] compressed = Reader.ReadBytes(compLen);
			byte[] raw = Helpers.Decompress(compressed, rawLen);

			BitArray zData = raw.ToBitArray();
			int[] zOrder = Helpers.GenerateZOrderIndices(Width, Height);
			Data = new BitArray(Width * Height);
			for (int i = 0; i < zOrder.Length; i++)
				Data[zOrder[i]] = zData[i];
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
			byte[] result = new byte[decodedLength];
			int ri = 0, di = 0;

			while (di < data.Length && ri < decodedLength)
			{
				byte header = data[di++];

				if ((header & 0x80) != 0)
				{
					int count = (header & 0x7F) + 2;
					byte val = data[di++];
					for (int j = 0; j < count && ri < decodedLength; j++)
						result[ri++] = val;
				}
				else
				{
					int count = (header & 0x7F) + 1;
					for (int j = 0; j < count && ri < decodedLength; j++)
						result[ri++] = data[di++];
				}
			}

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
			byte[] result = new byte[decodedLength];
			int ri = 0, di = 0;

			while (di < data.Length && ri < decodedLength)
			{
				byte header = data[di++];

				if ((header & 0x80) != 0)
				{
					int len = (header & 0x7F) + LZ_MIN_MATCH;
					int dist = data[di] | (data[di + 1] << 8);
					di += 2;
					int srcPos = ri - dist;
					for (int j = 0; j < len && ri < decodedLength; j++)
						result[ri++] = result[srcPos + j];
				}
				else
				{
					int len = (header & 0x7F) + 1;
					for (int j = 0; j < len && ri < decodedLength; j++)
						result[ri++] = data[di++];
				}
			}

			return result;
		}

		// --- Compress/Decompress: picks best of RLE vs LZ77 ---
		// Prefix byte: 0 = RLE, 1 = LZ77

		public static byte[] Compress(byte[] data)
		{
			byte[] rle = RleEncode(data);
			byte[] lz = Lz77Encode(data);

			if (rle.Length <= lz.Length + 1)
			{
				byte[] result = new byte[1 + rle.Length];
				result[0] = 0;
				Buffer.BlockCopy(rle, 0, result, 1, rle.Length);
				return result;
			}
			else
			{
				byte[] result = new byte[1 + lz.Length];
				result[0] = 1;
				Buffer.BlockCopy(lz, 0, result, 1, lz.Length);
				return result;
			}
		}

		public static byte[] Decompress(byte[] data, int decodedLength)
		{
			if (data.Length == 0) return new byte[decodedLength];
			byte mode = data[0];
			byte[] payload = new byte[data.Length - 1];
			Buffer.BlockCopy(data, 1, payload, 0, payload.Length);

			if (mode == 0)
				return RleDecode(payload, decodedLength);
			else
				return Lz77Decode(payload, decodedLength);
		}
	}
}
