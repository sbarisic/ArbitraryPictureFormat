using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace ArbitraryPictureFormat {
	class Program {
		static int Main(string[] args) {
			Console.OutputEncoding = System.Text.Encoding.UTF8;

			if (args.Length == 0 || args[0] == "--help" || args[0] == "-h") {
				PrintUsage();
				return args.Length == 0 ? 1 : 0;
			}

			string input = args[0];
			if (!File.Exists(input)) {
				Console.Error.WriteLine("Error: file not found: {0}", input);
				return 1;
			}

			string ext = Path.GetExtension(input).ToLowerInvariant();
			string dir = Path.GetDirectoryName(Path.GetFullPath(input));
			string name = Path.GetFileNameWithoutExtension(input);

			bool stencil = false;
			string output = null;

			for (int i = 1; i < args.Length; i++) {
				if (args[i] == "--stencil" || args[i] == "-s")
					stencil = true;
				else if ((args[i] == "--output" || args[i] == "-o") && i + 1 < args.Length)
					output = args[++i];
				else {
					Console.Error.WriteLine("Unknown option: {0}", args[i]);
					return 1;
				}
			}

			try {
				if (ext == ".apf") {
					output ??= Path.Combine(dir, name + ".png");
					DecodeApf(input, output, stencil);
				} else {
					output ??= Path.Combine(dir, name + ".apf");
					EncodeApf(input, output);
				}
			} catch (Exception ex) {
				Console.Error.WriteLine("Error: {0}", ex.Message);
				return 1;
			}

			return 0;
		}

		static void EncodeApf(string input, string output) {
			Console.WriteLine("  {0}", input);
			Image img = Image.FromFile(input);
			ArbitraryPicture apf = new ArbitraryPicture(img);
			apf.Save(output);

			long inSize = new FileInfo(input).Length;
			long outSize = new FileInfo(output).Length;
			double ratio = (double)outSize / inSize;
			Console.WriteLine("→ {0}  ({1:N0} → {2:N0} bytes, {3:F2}×)", output, inSize, outSize, ratio);
		}

		static void DecodeApf(string input, string output, bool stencil) {
			Console.WriteLine("  {0}", input);
			ArbitraryPicture apf = ArbitraryPicture.FromFile(input);

			using (Bitmap bmp = apf.ToBitmap())
				bmp.Save(output, ImageFormat.Png);
			Console.WriteLine("→ {0}", output);

			if (stencil) {
				string stencilPath = Path.Combine(
					Path.GetDirectoryName(output),
					Path.GetFileNameWithoutExtension(output) + "_stencil.png");
				using (Bitmap sbmp = apf.ToStencilBitmap())
					sbmp.Save(stencilPath, ImageFormat.Png);
				Console.WriteLine("→ {0}  (stencil)", stencilPath);
			}
		}

		static void PrintUsage() {
			Console.WriteLine("apf - Arbitrary Picture Format converter");
			Console.WriteLine();
			Console.WriteLine("Usage:");
			Console.WriteLine("  apf <image.png>              Encode PNG/BMP/etc to APF");
			Console.WriteLine("  apf <image.apf>              Decode APF to PNG");
			Console.WriteLine();
			Console.WriteLine("Options:");
			Console.WriteLine("  -o, --output <path>          Set output file path");
			Console.WriteLine("  -s, --stencil                Also export stencil mask (decode only)");
			Console.WriteLine("  -h, --help                   Show this help");
			Console.WriteLine();
			Console.WriteLine("Examples:");
			Console.WriteLine("  apf photo.png                → photo.apf");
			Console.WriteLine("  apf sprite.apf               → sprite.png");
			Console.WriteLine("  apf icon.apf -s              → icon.png + icon_stencil.png");
			Console.WriteLine("  apf logo.png -o out/logo.apf → out/logo.apf");
		}
	}
}