using System;
using System.Collections.Generic;
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

			string input = null;
			bool stencil = false;
			bool info = false;
			string output = null;
			string layer = null;

			for (int i = 0; i < args.Length; i++) {
				string a = args[i];
				if (a == "--stencil" || a == "-s")
					stencil = true;
				else if (a == "--info" || a == "-info" || a == "-i")
					info = true;
				else if ((a == "--output" || a == "-o") && i + 1 < args.Length)
					output = args[++i];
				else if ((a == "--layer" || a == "-l") && i + 1 < args.Length)
					layer = args[++i];
				else if (a.StartsWith("-")) {
					Console.Error.WriteLine("Unknown option: {0}", a);
					return 1;
				} else if (input == null)
					input = a;
				else {
					Console.Error.WriteLine("Unexpected argument: {0}", a);
					return 1;
				}
			}

			if (input == null) {
				Console.Error.WriteLine("Error: no input file specified");
				return 1;
			}

			if (!File.Exists(input)) {
				Console.Error.WriteLine("Error: file not found: {0}", input);
				return 1;
			}

			string ext = Path.GetExtension(input).ToLowerInvariant();
			string dir = Path.GetDirectoryName(Path.GetFullPath(input));
			string name = Path.GetFileNameWithoutExtension(input);

			try {
				if (ext == ".apf") {
					if (info) {
						PrintInfo(input);
					} else {
						output ??= Path.Combine(dir, name + ".png");
						if (!DecodeApf(input, output, stencil, layer))
							return 1;
					}
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
			var file = ApfFile.FromSingleImage(apf);
			file.Save(output);

			long inSize = new FileInfo(input).Length;
			long outSize = new FileInfo(output).Length;
			double ratio = (double)outSize / inSize;
			Console.WriteLine("→ {0}  ({1:N0} → {2:N0} bytes, {3:F2}×)", output, inSize, outSize, ratio);
		}

		static bool DecodeApf(string input, string output, bool stencil, string layer) {
			Console.WriteLine("  {0}", input);
			ApfFile file = ApfFile.FromFile(input);

			if (file.Images.Count > 1)
				Console.WriteLine("  {0} images: {1}", file.Images.Count,
					string.Join(", ", file.Images.ConvertAll(i => string.IsNullOrEmpty(i.Name) ? "(unnamed)" : i.Name)));

			ApfImage image = file.GetImage(layer);
			if (image == null) {
				Console.Error.WriteLine("Error: no image found" + (layer != null ? $" with name '{layer}'" : ""));
				return false;
			}

			if (!string.IsNullOrEmpty(image.Name))
				Console.WriteLine("  Layer: {0}", image.Name);

			using (Bitmap bmp = image.Picture.ToBitmap())
				bmp.Save(output, ImageFormat.Png);
			Console.WriteLine("→ {0}", output);

			if (stencil) {
				string stencilPath = Path.Combine(
					Path.GetDirectoryName(output),
					Path.GetFileNameWithoutExtension(output) + "_stencil.png");
				using (Bitmap sbmp = image.Picture.ToStencilBitmap())
					sbmp.Save(stencilPath, ImageFormat.Png);
				Console.WriteLine("→ {0}  (stencil)", stencilPath);
			}

			return true;
		}

		static void PrintInfo(string input) {
			ApfFile file = ApfFile.FromFile(input);
			long fileSize = new FileInfo(input).Length;

			byte version;
			using (var fs = File.OpenRead(input))
				version = (byte)fs.ReadByte();
			string verStr = version switch {
				0x10 => "1.0",
				0x11 => "1.1",
				0x20 => "2.0",
				_ => $"0x{version:X2}"
			};

			Console.WriteLine("  File:    {0}", Path.GetFullPath(input));
			Console.WriteLine("  Size:    {0:N0} bytes", fileSize);
			Console.WriteLine("  Version: {0}", verStr);
			Console.WriteLine("  Images:  {0}", file.Images.Count);
			Console.WriteLine();

			for (int i = 0; i < file.Images.Count; i++) {
				ApfImage img = file.Images[i];
				string name = string.IsNullOrEmpty(img.Name) ? "(unnamed)" : img.Name;
				var desc = img.Picture.Descriptor;
				var bg = img.Picture.Background;

				Console.WriteLine("  [{0}] {1}", i, name);
				Console.WriteLine("      Dimensions: {0}×{1}", desc.Width, desc.Height);
				Console.WriteLine("      Pixels:     {0:N0} ({1:N0} in shape)",
					desc.Width * desc.Height, img.Picture.ImageData.Length);
				Console.WriteLine("      Background: #{0:X2}{1:X2}{2:X2}{3:X2}",
					bg.A, bg.R, bg.G, bg.B);

				if (img.HasMetadata && img.Metadata.Count > 0) {
					Console.WriteLine("      Metadata:");
					foreach (var kvp in img.Metadata)
						Console.WriteLine("        {0} = {1}", kvp.Key, kvp.Value);
				}
				Console.WriteLine();
			}
		}

		static void PrintUsage() {
			Console.WriteLine("apf - Arbitrary Picture Format converter");
			Console.WriteLine();
			Console.WriteLine("Usage:");
			Console.WriteLine("  apf <image.png>              Encode PNG/BMP/etc to APF");
			Console.WriteLine("  apf <image.apf>              Decode APF to PNG");
			Console.WriteLine("  apf -info <image.apf>        Show image/metadata info");
			Console.WriteLine();
			Console.WriteLine("Options:");
			Console.WriteLine("  -o, --output <path>          Set output file path");
			Console.WriteLine("  -s, --stencil                Also export stencil mask (decode only)");
			Console.WriteLine("  -l, --layer <name>           Select image by name (multi-image APF)");
			Console.WriteLine("  -i, -info, --info            Print file info and metadata");
			Console.WriteLine("  -h, --help                   Show this help");
			Console.WriteLine();
			Console.WriteLine("Examples:");
			Console.WriteLine("  apf photo.png                → photo.apf");
			Console.WriteLine("  apf sprite.apf               → sprite.png");
			Console.WriteLine("  apf icon.apf -s              → icon.png + icon_stencil.png");
			Console.WriteLine("  apf model.apf -l normal      → extract 'normal' layer");
			Console.WriteLine("  apf -info model.apf          → list images and metadata");
			Console.WriteLine("  apf logo.png -o out/logo.apf → out/logo.apf");
		}
	}
}
