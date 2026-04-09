using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ArbitraryPictureFormat {
	[Flags]
	enum ClrFmt : int {
		None = 0,
		Mono = 1 << 0,
		R = 1 << 1,
		G = 1 << 2,
		B = 1 << 3,
		A = 1 << 4,

		RGB = R | G | B,
		RGBA = R | G | B | A,
	}

	class ArbitraryPicture {
		public ClrFmt ColorFormat;
		public Color Background;
		public Color DefaultColor;
		public ShapeDesc Descriptor;
		public Color[] ImageData;

		public static ArbitraryPicture FromFile(string FilePath) {
			using (FileStream FS = File.OpenRead(FilePath))
				return new ArbitraryPicture(FS);
		}

		public ArbitraryPicture(ShapeDesc Descriptor, Color Background) {
			this.Background = Background;
			this.Descriptor = Descriptor;
			this.ColorFormat = ClrFmt.RGBA;

			ImageData = new Color[Descriptor.GetCount()];
		}

		public ArbitraryPicture(Image Img) {
			Descriptor = new ShapeDesc(Img.Width, Img.Height);

			using (Bitmap Bmp = new Bitmap(Img)) {
				BitmapData BmpData = Bmp.LockBits();

				// Find most common color to use as background
				Dictionary<int, int> colorCounts = new Dictionary<int, int>();
				for (int y = 0; y < Img.Height; y++) {
					for (int x = 0; x < Img.Width; x++) {
						int argb = BmpData.GetPixelArgb(x, y);
						if (colorCounts.ContainsKey(argb))
							colorCounts[argb]++;
						else
							colorCounts[argb] = 1;
					}
				}

				int bestArgb = 0;
				int bestCount = 0;
				foreach (var kvp in colorCounts) {
					if (kvp.Value > bestCount) {
						bestCount = kvp.Value;
						bestArgb = kvp.Key;
					}
				}
				Background = Color.FromArgb(bestArgb);

				for (int y = 0; y < Img.Height; y++) {
					for (int x = 0; x < Img.Width; x++) {
						Color C = BmpData.GetPixel(x, y);
						Descriptor.Set(x, y, C != Background);
					}
				}

				ImageData = new Color[Descriptor.GetCount()];

				int Idx = 0;
				for (int y = 0; y < Img.Height; y++) {
					for (int x = 0; x < Img.Width; x++) {
						if (Descriptor.Get(x, y))
							ImageData[Idx++] = BmpData.GetPixel(x, y);
					}
				}

				Bmp.UnlockBits(BmpData);
			}

			CalculateColorFormat();
		}

		public void CalculateColorFormat() {
			bool Mono = true, R = false, G = false, B = false, A = false;
			byte Def_A = ImageData[0].A;
			byte Def_R = ImageData[0].R;
			byte Def_G = ImageData[0].G;
			byte Def_B = ImageData[0].B;

			for (int i = 0; i < ImageData.Length; i++) {
				Color C = ImageData[i];

				if (!(C.R == C.G && C.R == C.B))
					Mono = false;

				if (C.R != Def_R)
					R = true;
				if (C.G != Def_G)
					G = true;
				if (C.B != Def_B)
					B = true;
				if (C.A != Def_A)
					A = true;
			}

			DefaultColor = Color.FromArgb(A ? 0 : Def_A, R || Mono ? 0 : Def_R, G || Mono ? 0 : Def_G, B || Mono ? 0 : Def_B);
			ColorFormat = (Mono ? ClrFmt.Mono : ((R ? ClrFmt.R : ClrFmt.None) | (G ? ClrFmt.G : ClrFmt.None) | (B ? ClrFmt.B : ClrFmt.None))) | (A ? ClrFmt.A : ClrFmt.None);
		}

		public ArbitraryPicture(Stream S) {
			Deserialize(S);
		}

		public Bitmap ToStencilBitmap() {
			Bitmap Bmp = new Bitmap(Descriptor.Width, Descriptor.Height);
			BitmapData BmpData = Bmp.LockBits();

			for (int y = 0; y < Descriptor.Height; y++)
				for (int x = 0; x < Descriptor.Width; x++)
					BmpData.SetPixel(x, y, Descriptor.Get(x, y) ? Color.White : Color.Black);

			Bmp.UnlockBits(BmpData);
			return Bmp;
		}

		public Bitmap ToBitmap(Color Background) {
			Bitmap Bmp = new Bitmap(Descriptor.Width, Descriptor.Height);
			BitmapData BmpData = Bmp.LockBits();

			int Idx = 0;
			for (int y = 0; y < Descriptor.Height; y++)
				for (int x = 0; x < Descriptor.Width; x++) {
					BmpData.SetPixel(x, y, Descriptor.Get(x, y) ? ImageData[Idx++] : Background);
				}

			Bmp.UnlockBits(BmpData);
			return Bmp;
		}

		public Bitmap ToBitmap() {
			return ToBitmap(Color.Transparent);
		}

		public void Save(string FilePath) {
			/*string Ext = Path.GetExtension(FilePath);
			FilePath = Path.GetFileNameWithoutExtension(FilePath);*/

			using (FileStream FS = File.OpenWrite(FilePath))
				Serialize(FS);
		}

		const byte FORMAT_VERSION = 0x03;

		public void Serialize(Stream S) {
			using (BinaryWriter Writer = new BinaryWriter(S, Encoding.UTF8, true)) {
				Writer.Write(FORMAT_VERSION);
				Descriptor.Serialize(Writer);

				Writer.Write((int)ColorFormat);
				Writer.Write(Background.ToArgb());
				Writer.Write(DefaultColor.ToArgb());
				Writer.Write(ImageData.Length);

				// Reorder pixel data to Z-order for better spatial locality
				int[] zOrder = Helpers.GenerateZOrderIndices(Descriptor.Width, Descriptor.Height);
				Color[] zPixels = ReorderPixelsToZOrder(zOrder);

				// Build per-channel planes, delta-encode, then RLE
				WriteChannelPlane(Writer, zPixels, ColorFormat, ClrFmt.Mono, ClrFmt.R, c => c.R);
				WriteChannelPlane(Writer, zPixels, ColorFormat, ClrFmt.None, ClrFmt.G, c => c.G);
				WriteChannelPlane(Writer, zPixels, ColorFormat, ClrFmt.None, ClrFmt.B, c => c.B);
				WriteChannelPlane(Writer, zPixels, ColorFormat, ClrFmt.None, ClrFmt.A, c => c.A);
			}
		}

		Color[] ReorderPixelsToZOrder(int[] zOrder) {
			// Map scanline index → ImageData index for active pixels
			int[] scanToImg = new int[Descriptor.Width * Descriptor.Height];
			int imgIdx = 0;
			for (int i = 0; i < scanToImg.Length; i++)
				scanToImg[i] = Descriptor.Data[i] ? imgIdx++ : -1;

			Color[] zPixels = new Color[ImageData.Length];
			int zi = 0;
			for (int i = 0; i < zOrder.Length; i++) {
				int si = scanToImg[zOrder[i]];
				if (si >= 0)
					zPixels[zi++] = ImageData[si];
			}
			return zPixels;
		}

		void ReorderPixelsFromZOrder(int[] zOrder, Color[] zPixels) {
			int[] scanToImg = new int[Descriptor.Width * Descriptor.Height];
			int imgIdx = 0;
			for (int i = 0; i < scanToImg.Length; i++)
				scanToImg[i] = Descriptor.Data[i] ? imgIdx++ : -1;

			ImageData = new Color[zPixels.Length];
			int zi = 0;
			for (int i = 0; i < zOrder.Length; i++) {
				int si = scanToImg[zOrder[i]];
				if (si >= 0)
					ImageData[si] = zPixels[zi++];
			}
		}

		static void WriteChannelPlane(BinaryWriter Writer, Color[] data, ClrFmt fmt, ClrFmt altFlag, ClrFmt flag, Func<Color, byte> selector) {
			if (!fmt.HasFlag(flag) && !fmt.HasFlag(altFlag))
				return;

			byte[] plane = new byte[data.Length];
			for (int i = 0; i < data.Length; i++)
				plane[i] = selector(data[i]);

			byte[] delta = Helpers.DeltaEncode(plane);
			byte[] rle = Helpers.RleEncode(delta);

			Writer.Write(rle.Length);
			Writer.Write(rle);
		}

		void Deserialize(Stream S) {
			using (BinaryReader Reader = new BinaryReader(S, Encoding.UTF8, true)) {
				byte version = Reader.ReadByte();

				if (version == FORMAT_VERSION)
					DeserializeV2(Reader);
				else
					throw new InvalidDataException("Unknown APF format version: 0x" + version.ToString("X2"));
			}
		}

		void DeserializeV2(BinaryReader Reader) {
			Descriptor = ShapeDesc.FromStream(Reader);

			ColorFormat = (ClrFmt)Reader.ReadInt32();
			Background = Color.FromArgb(Reader.ReadInt32());
			DefaultColor = Color.FromArgb(Reader.ReadInt32());

			int pixelCount = Reader.ReadInt32();

			byte[] rPlane = ReadChannelPlane(Reader, pixelCount, ColorFormat, ClrFmt.Mono, ClrFmt.R, DefaultColor.R);
			byte[] gPlane = ReadChannelPlane(Reader, pixelCount, ColorFormat, ClrFmt.None, ClrFmt.G, DefaultColor.G);
			byte[] bPlane = ReadChannelPlane(Reader, pixelCount, ColorFormat, ClrFmt.None, ClrFmt.B, DefaultColor.B);
			byte[] aPlane = ReadChannelPlane(Reader, pixelCount, ColorFormat, ClrFmt.None, ClrFmt.A, DefaultColor.A);

			Color[] zPixels = new Color[pixelCount];
			for (int i = 0; i < pixelCount; i++) {
				byte r = rPlane[i], g = gPlane[i], b = bPlane[i], a = aPlane[i];

				if (ColorFormat.HasFlag(ClrFmt.Mono))
					g = b = r;

				zPixels[i] = Color.FromArgb(a, r, g, b);
			}

			// Reorder from Z-order back to scanline order
			int[] zOrder = Helpers.GenerateZOrderIndices(Descriptor.Width, Descriptor.Height);
			ReorderPixelsFromZOrder(zOrder, zPixels);
		}

		static byte[] ReadChannelPlane(BinaryReader Reader, int pixelCount, ClrFmt fmt, ClrFmt altFlag, ClrFmt flag, byte defaultVal) {
			if (!fmt.HasFlag(flag) && !fmt.HasFlag(altFlag)) {
				byte[] fill = new byte[pixelCount];
				for (int i = 0; i < pixelCount; i++)
					fill[i] = defaultVal;
				return fill;
			}

			int rleLen = Reader.ReadInt32();
			byte[] rle = Reader.ReadBytes(rleLen);
			byte[] delta = Helpers.RleDecode(rle, pixelCount);
			return Helpers.DeltaDecode(delta);
		}
	}

	struct ShapeDesc {
		public int Width, Height;
		public BitArray Data;

		public static ShapeDesc FromStream(Stream S) {
			ShapeDesc H = new ShapeDesc();
			H.Deserialize(S);
			return H;
		}

		public static ShapeDesc FromStream(BinaryReader Reader) {
			ShapeDesc H = new ShapeDesc();
			H.Deserialize(Reader);
			return H;
		}

		public ShapeDesc(int Width, int Height) {
			this.Width = Width;
			this.Height = Height;
			Data = new BitArray(Width * Height);
			Data.SetAll(false);
		}

		public int GetCount() {
			int C = 0;
			for (int i = 0; i < Data.Length; i++)
				C += Data[i] ? 1 : 0;
			return C;
		}

		public bool Get(int X, int Y) {
			return Data[Y * Width + X];
		}

		public void Set(int X, int Y, bool Val) {
			Data[Y * Width + X] = Val;
		}

		public void Serialize(Stream S) {
			using (BinaryWriter Writer = new BinaryWriter(S, Encoding.UTF8, true)) {
				Serialize(Writer);
			}
		}

		public void Serialize(BinaryWriter Writer) {
			Writer.Write(Width);
			Writer.Write(Height);

			// Reorder stencil bits to Z-order for better spatial compression
			int[] zOrder = Helpers.GenerateZOrderIndices(Width, Height);
			BitArray zData = new BitArray(Data.Length);
			for (int i = 0; i < zOrder.Length; i++)
				zData[i] = Data[zOrder[i]];

			byte[] DataArray = zData.ToByteArray();
			byte[] rle = Helpers.RleEncode(DataArray);

			Writer.Write(DataArray.Length);
			Writer.Write(rle.Length);
			Writer.Write(rle);
		}

		void Deserialize(Stream S) {
			using (BinaryReader Reader = new BinaryReader(S, Encoding.UTF8, true)) {
				Deserialize(Reader);
			}
		}

		void Deserialize(BinaryReader Reader) {
			Width = Reader.ReadInt32();
			Height = Reader.ReadInt32();

			int rawLen = Reader.ReadInt32();
			int rleLen = Reader.ReadInt32();
			byte[] rle = Reader.ReadBytes(rleLen);
			byte[] DataArray = Helpers.RleDecode(rle, rawLen);

			// Reorder from Z-order back to scanline
			BitArray zData = DataArray.ToBitArray();
			int[] zOrder = Helpers.GenerateZOrderIndices(Width, Height);
			Data = new BitArray(Width * Height);
			for (int i = 0; i < zOrder.Length; i++)
				Data[zOrder[i]] = zData[i];
		}
	}

	static class Helpers {
		public static byte[] ToByteArray(this BitArray BA) {
			byte[] Arr = new byte[(int)Math.Ceiling((double)BA.Length / (sizeof(byte) * 8))];
			BA.CopyTo(Arr, 0);
			return Arr;
		}

		public static BitArray ToBitArray(this byte[] Vals) {
			return new BitArray(Vals);
		}

		public static void WriteBytes(this Stream S, byte[] Bytes) {
			for (int i = 0; i < Bytes.Length; i++)
				S.WriteByte(Bytes[i]);
		}

		public static byte[] ReadBytes(this Stream S, int Len) {
			byte[] Data = new byte[Len];

			for (int i = 0; i < Data.Length; i++)
				Data[i] = (byte)S.ReadByte();

			return Data;
		}

		public static bool IsHomogenous(this byte[] Bytes) {
			byte B = Bytes[0];

			for (int i = 0; i < Bytes.Length; i++)
				if (Bytes[i] != B)
					return false;

			return true;
		}

		public static BitmapData LockBits(this Bitmap Bmp) {
			return Bmp.LockBits(new Rectangle(0, 0, Bmp.Width, Bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
		}

		public static Color GetPixel(this BitmapData Data, int X, int Y) {
			return Color.FromArgb(Marshal.ReadInt32(Data.Scan0, Y * Data.Stride + X * 4));
		}

		public static int GetPixelArgb(this BitmapData Data, int X, int Y) {
			return Marshal.ReadInt32(Data.Scan0, Y * Data.Stride + X * 4);
		}

		public static void SetPixel(this BitmapData Data, int X, int Y, Color Clr) {
			Marshal.WriteInt32(Data.Scan0, Y * Data.Stride + X * 4, Clr.ToArgb());
		}

		// Flag-bit RLE: high bit set = repeat run, high bit clear = literal run
		// Repeat: [0x80 | (count-2)] [value]  — count 2..129
		// Literal: [count-1] [byte0] [byte1] ... — count 1..128
		public static byte[] RleEncode(byte[] data) {
			if (data.Length == 0) return new byte[0];

			List<byte> result = new List<byte>();
			int i = 0;

			while (i < data.Length) {
				// Check for a repeat run
				int runLen = 1;
				while (i + runLen < data.Length && data[i + runLen] == data[i] && runLen < 129)
					runLen++;

				if (runLen >= 2) {
					result.Add((byte)(0x80 | (runLen - 2)));
					result.Add(data[i]);
					i += runLen;
				} else {
					// Collect literal bytes
					int litStart = i;
					int litLen = 0;

					while (i < data.Length && litLen < 128) {
						// Peek ahead: if next 2+ bytes are the same, stop the literal run
						if (i + 1 < data.Length && data[i] == data[i + 1])
							break;
						litLen++;
						i++;
					}

					if (litLen == 0) {
						// Single byte that starts a repeat — emit as literal of length 1
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

		public static byte[] RleDecode(byte[] data, int decodedLength) {
			byte[] result = new byte[decodedLength];
			int ri = 0, di = 0;

			while (di < data.Length && ri < decodedLength) {
				byte header = data[di++];

				if ((header & 0x80) != 0) {
					// Repeat run
					int count = (header & 0x7F) + 2;
					byte val = data[di++];
					for (int j = 0; j < count && ri < decodedLength; j++)
						result[ri++] = val;
				} else {
					// Literal run
					int count = (header & 0x7F) + 1;
					for (int j = 0; j < count && ri < decodedLength; j++)
						result[ri++] = data[di++];
				}
			}

			return result;
		}

		public static byte[] DeltaEncode(byte[] data) {
			if (data.Length == 0) return new byte[0];

			byte[] result = new byte[data.Length];
			result[0] = data[0];
			for (int i = 1; i < data.Length; i++)
				result[i] = (byte)(data[i] - data[i - 1]);
			return result;
		}

		public static byte[] DeltaDecode(byte[] data) {
			if (data.Length == 0) return new byte[0];

			byte[] result = new byte[data.Length];
			result[0] = data[0];
			for (int i = 1; i < data.Length; i++)
				result[i] = (byte)(data[i] + result[i - 1]);
			return result;
		}

		static uint MortonSpread(uint x) {
			x = (x | (x << 8)) & 0x00FF00FF;
			x = (x | (x << 4)) & 0x0F0F0F0F;
			x = (x | (x << 2)) & 0x33333333;
			x = (x | (x << 1)) & 0x55555555;
			return x;
		}

		static uint MortonCompact(uint x) {
			x &= 0x55555555;
			x = (x | (x >> 1)) & 0x33333333;
			x = (x | (x >> 2)) & 0x0F0F0F0F;
			x = (x | (x >> 4)) & 0x00FF00FF;
			x = (x | (x >> 8)) & 0x0000FFFF;
			return x;
		}

		public static uint MortonEncode(uint x, uint y) {
			return MortonSpread(x) | (MortonSpread(y) << 1);
		}

		public static void MortonDecode(uint code, out uint x, out uint y) {
			x = MortonCompact(code);
			y = MortonCompact(code >> 1);
		}

		// Returns array where result[i] = scanline index of the i-th pixel in Z-order
		public static int[] GenerateZOrderIndices(int width, int height) {
			int total = width * height;
			int[] indices = new int[total];
			uint[] mortonCodes = new uint[total];

			for (int y = 0; y < height; y++) {
				for (int x = 0; x < width; x++) {
					int scanIdx = y * width + x;
					indices[scanIdx] = scanIdx;
					mortonCodes[scanIdx] = MortonEncode((uint)x, (uint)y);
				}
			}

			Array.Sort(mortonCodes, indices);
			return indices;
		}
	}
}
