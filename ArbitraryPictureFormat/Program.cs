using System;
using System.Collections;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Drawing;

namespace ArbitraryPictureFormat {
	class Program {
		static void Main(string[] args) {
			string Ext = Path.GetExtension(args[0]).ToLower();
			string FName = Path.GetFileNameWithoutExtension(args[0]);

			if (Ext != ".apf") {
				Console.WriteLine("Loading image");
				Image Img = Image.FromFile(args[0]);

				Console.WriteLine("Converting to .apf");
				ArbitraryPicture APF = new ArbitraryPicture(Img);

				Console.WriteLine("Writing to {0}.apf", FName);
				using (FileStream FS = File.OpenWrite(FName + ".apf"))
					APF.Serialize(FS);
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

	class ArbitraryPicture {
		public ShapeDesc Descriptor;
		public Color[] ImageData;

		public ArbitraryPicture(ShapeDesc Descriptor) {
			this.Descriptor = Descriptor;
			ImageData = new Color[Descriptor.GetCount()];
		}

		public ArbitraryPicture(Image Img) {
			Descriptor = new ShapeDesc(Img.Width, Img.Height);

			using (Bitmap Bmp = new Bitmap(Img)) {
				for (int y = 0; y < Img.Height; y++)
					for (int x = 0; x < Img.Width; x++) {
						Color C = Bmp.GetPixel(x, y);
						Descriptor.Set(x, y, C.A != 0);
					}

				ImageData = new Color[Descriptor.GetCount()];

				int Idx = 0;
				for (int y = 0; y < Img.Height; y++)
					for (int x = 0; x < Img.Width; x++) {
						if (Descriptor.Get(x, y))
							ImageData[Idx++] = Bmp.GetPixel(x, y);
					}
			}
		}

		public ArbitraryPicture(Stream S) {
			using (DeflateStream Deflate = new DeflateStream(S, CompressionMode.Decompress)) {
				Descriptor = new ShapeDesc(Deflate);

				using (BinaryReader Reader = new BinaryReader(Deflate, Encoding.UTF8, true)) {
					ImageData = new Color[Reader.ReadInt32()];

					for (int i = 0; i < ImageData.Length; i++)
						ImageData[i] = Color.FromArgb(Reader.ReadInt32());
				}
			}
		}

		public Bitmap ToStencilBitmap() {
			Bitmap Bmp = new Bitmap(Descriptor.Width, Descriptor.Height);

			for (int y = 0; y < Descriptor.Height; y++)
				for (int x = 0; x < Descriptor.Width; x++)
					Bmp.SetPixel(x, y, Descriptor.Get(x, y) ? Color.White : Color.Black);

			return Bmp;
		}

		public Bitmap ToBitmap(Color Background) {
			Bitmap Bmp = new Bitmap(Descriptor.Width, Descriptor.Height);

			int Idx = 0;
			for (int y = 0; y < Descriptor.Height; y++)
				for (int x = 0; x < Descriptor.Width; x++) {
					Bmp.SetPixel(x, y, Descriptor.Get(x, y) ? ImageData[Idx++] : Background);
				}

			return Bmp;
		}

		public Bitmap ToBitmap() {
			return ToBitmap(Color.Transparent);
		}

		public void Serialize(Stream S) {
			using (DeflateStream Deflate = new DeflateStream(S, CompressionLevel.Optimal, true)) {
				Descriptor.Serialize(Deflate);

				using (BinaryWriter Writer = new BinaryWriter(Deflate, Encoding.UTF8, true)) {
					Writer.Write(ImageData.Length);
					for (int i = 0; i < ImageData.Length; i++)
						Writer.Write(ImageData[i].ToArgb());
				}
			}
		}
	}

	class ShapeDesc {
		public int Width, Height;
		public BitArray Data;

		public ShapeDesc(int Width, int Height) {
			this.Width = Width;
			this.Height = Height;
			Data = new BitArray(Width * Height);
			Data.SetAll(false);
		}

		public ShapeDesc(Stream S) {
			Deserialize(S);
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
				Writer.Write(Width);
				Writer.Write(Height);
				byte[] DataArray = Data.ToByteArray();
				Writer.Write(DataArray.Length);
				Writer.Write(DataArray);
			}
		}

		public void Deserialize(Stream S) {
			using (BinaryReader Reader = new BinaryReader(S, Encoding.UTF8, true)) {
				Width = Reader.ReadInt32();
				Height = Reader.ReadInt32();
				byte[] DataArray = Reader.ReadBytes(Reader.ReadInt32());
				Data = DataArray.ToBitArray();
			}
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
	}
}