using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace ArbitraryPictureFormat {
	class Program {
		static void Main(string[] args) {
			string InputDir = "data/png";
			string OutputDir = "data/apf";

			if (Debugger.IsAttached) {
				Directory.CreateDirectory(OutputDir);

				string[] Files = Directory.GetFiles(InputDir, "*.png");

				for (int i = 0; i < Files.Length; i++) {
					string Name = Path.GetFileNameWithoutExtension(Files[i]);
					Console.WriteLine("Testing {0}", Name);

					Image Test = Image.FromFile(Files[i]);

					string ApfPath = Path.Combine(OutputDir, Name + ".apf");
					ArbitraryPicture APF = new ArbitraryPicture(Test);
					APF.Save(ApfPath);

					// Save a raw BMP for size comparison
					using (Bitmap bmp = new Bitmap(Test))
						bmp.Save(Path.Combine(OutputDir, Name + ".bmp"), ImageFormat.Bmp);

					APF = ArbitraryPicture.FromFile(ApfPath);
					APF.ToStencilBitmap().Save(Path.Combine(OutputDir, Name + "_stencil.png"));
					APF.ToBitmap().Save(Path.Combine(OutputDir, Name + "_out.png"));
				}
				Console.WriteLine("Done!");
				return;
			}

			string Ext = Path.GetExtension(args[0]).ToLower();
			string FName = Path.GetFileNameWithoutExtension(args[0]);
			Directory.CreateDirectory(OutputDir);

			if (Ext != ".apf") {
				Console.WriteLine("Loading image");
				Image Img = Image.FromFile(args[0]);

				Console.WriteLine("Converting to .apf");
				ArbitraryPicture APF = new ArbitraryPicture(Img);

				string OutPath = Path.Combine(OutputDir, FName + ".apf");
				Console.WriteLine("Writing to {0}", OutPath);
				APF.Save(OutPath);
			} else {
				ArbitraryPicture APF;

				Console.WriteLine("Loading .apf");
				using (FileStream FS = File.OpenRead(args[0]))
					APF = new ArbitraryPicture(FS);

				if (args.Length == 2 && (args[1] == "--stencil" || args[1] == "-s")) {
					string StencilPath = Path.Combine(OutputDir, FName + "_stencil.png");
					Console.WriteLine("Writing {0}", StencilPath);
					APF.ToStencilBitmap().Save(StencilPath);
				}

				string PngPath = Path.Combine(OutputDir, FName + ".png");
				Console.WriteLine("Writing {0}", PngPath);
				APF.ToBitmap().Save(PngPath);
			}
		}
	}
}