using System;
using System.Collections;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using System.Drawing.Imaging;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ArbitraryPictureFormat {
	class Program {
		static void Main(string[] args) {
			if (Debugger.IsAttached) {
				string[] Files = new string[] { "tests/crysis", "tests/r", "tests/g", "tests/b", "tests/gb", "tests/mono", "tests/noise1", "tests/shapetest", "tests/planet" };

				for (int i = 0; i < Files.Length; i++) {
					string Name = Files[i];
					Console.WriteLine("Testing {0}", Name);

					Image Test = Image.FromFile(Name + ".png");

					ArbitraryPicture APF = new ArbitraryPicture(Test);
					APF.Save(Name + ".apf");

					APF = ArbitraryPicture.FromFile(Name + ".apf");
					APF.ToStencilBitmap().Save(Name + "_stencil.png");
					APF.ToBitmap().Save(Name + "_out.png");
				}
				Console.WriteLine("Done!");
				return;
			}

			string Ext = Path.GetExtension(args[0]).ToLower();
			string FName = Path.GetFileNameWithoutExtension(args[0]);

			if (Ext != ".apf") {
				Console.WriteLine("Loading image");
				Image Img = Image.FromFile(args[0]);

				Console.WriteLine("Converting to .apf");
				ArbitraryPicture APF = new ArbitraryPicture(Img);

				Console.WriteLine("Writing to {0}.apf", FName);
				APF.Save(FName + ".apf");
			} else {
				ArbitraryPicture APF;

				Console.WriteLine("Loading .apf");
				using (FileStream FS = File.OpenRead(args[0]))
					APF = new ArbitraryPicture(FS);

				if (args.Length == 2 && (args[1] == "--stencil" || args[1] == "-s")) {
					Console.WriteLine("Writing {0}_stencil.png", FName);
					APF.ToStencilBitmap().Save(FName + "_stencil.png");
				}

				Console.WriteLine("Writing {0}.png", FName);
				APF.ToBitmap().Save(FName + ".png");
			}
		}

	}

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
			//ColorFormat = ClrFmt.RGBA;
			Background = Color.Transparent;
			Descriptor = new ShapeDesc(Img.Width, Img.Height);

			using (Bitmap Bmp = new Bitmap(Img)) {
				BitmapData BmpData = Bmp.LockBits();

				// todo: get most common color

				for (int y = 0; y < Img.Height; y++) {
					for (int x = 0; x < Img.Width; x++) {
						Color C = BmpData.GetPixel(x, y);
						Descriptor.Set(x, y, Background.A == 0 ? C.A != 0 : (C != Background));
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

		public void Serialize(Stream S) {
			using (DeflateStream Deflate = new DeflateStream(S, CompressionLevel.Optimal, true)) {
				using (BinaryWriter Writer = new BinaryWriter(Deflate, Encoding.UTF8, true)) {
					Descriptor.Serialize(Writer);

					Writer.Write((int)ColorFormat);
					Writer.Write(Background.ToArgb());
					Writer.Write(DefaultColor.ToArgb());

					Writer.Write(ImageData.Length);
					for (int i = 0; i < ImageData.Length; i++) {
						if (ColorFormat.HasFlag(ClrFmt.Mono) || ColorFormat.HasFlag(ClrFmt.R))
							Writer.Write(ImageData[i].R);

						if (ColorFormat.HasFlag(ClrFmt.G))
							Writer.Write(ImageData[i].G);

						if (ColorFormat.HasFlag(ClrFmt.B))
							Writer.Write(ImageData[i].B);

						if (ColorFormat.HasFlag(ClrFmt.A))
							Writer.Write(ImageData[i].A);

						//Writer.Write(ImageData[i].ToArgb());
					}
				}
			}
		}

		void Deserialize(Stream S) {
			using (DeflateStream Deflate = new DeflateStream(S, CompressionMode.Decompress)) {
				using (BinaryReader Reader = new BinaryReader(Deflate, Encoding.UTF8, true)) {
					Descriptor = ShapeDesc.FromStream(Reader);

					ColorFormat = (ClrFmt)Reader.ReadInt32();
					Background = Color.FromArgb(Reader.ReadInt32());
					DefaultColor = Color.FromArgb(Reader.ReadInt32());

					ImageData = new Color[Reader.ReadInt32()];
					for (int i = 0; i < ImageData.Length; i++) {
						byte R = DefaultColor.R, G = DefaultColor.G, B = DefaultColor.B, A = DefaultColor.A;

						if (ColorFormat.HasFlag(ClrFmt.Mono) || ColorFormat.HasFlag(ClrFmt.R))
							R = Reader.ReadByte();

						if (ColorFormat.HasFlag(ClrFmt.G))
							G = Reader.ReadByte();

						if (ColorFormat.HasFlag(ClrFmt.B))
							B = Reader.ReadByte();

						if (ColorFormat.HasFlag(ClrFmt.A))
							A = Reader.ReadByte();

						if (ColorFormat.HasFlag(ClrFmt.Mono))
							G = B = R;

						ImageData[i] = Color.FromArgb(A, R, G, B);
					}
				}
			}
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

			byte[] DataArray = Data.ToByteArray();
			bool Homo = DataArray.IsHomogenous();

			Writer.Write(Homo);
			Writer.Write(DataArray.Length);

			if (Homo)
				Writer.Write(DataArray[0]);
			else
				Writer.Write(DataArray);
		}

		void Deserialize(Stream S) {
			using (BinaryReader Reader = new BinaryReader(S, Encoding.UTF8, true)) {
				Deserialize(Reader);
			}
		}

		void Deserialize(BinaryReader Reader) {
			Width = Reader.ReadInt32();
			Height = Reader.ReadInt32();

			bool Homo = Reader.ReadBoolean();
			int Len = Reader.ReadInt32();
			byte[] DataArray;

			if (Homo) {
				DataArray = new byte[Len];
				byte B = Reader.ReadByte();

				for (int i = 0; i < Len; i++)
					DataArray[i] = B;
			} else
				DataArray = Reader.ReadBytes(Len);

			Data = DataArray.ToBitArray();
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

		public static void SetPixel(this BitmapData Data, int X, int Y, Color Clr) {
			Marshal.WriteInt32(Data.Scan0, Y * Data.Stride + X * 4, Clr.ToArgb());
		}
	}
}