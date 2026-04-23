using System.Drawing;
using ArbitraryPictureFormat;

namespace ArbitraryPictureFormat.Tests;

public class FileSizeTests
{
	static string DataDir => Path.Combine(AppContext.BaseDirectory, "data", "png");
	static string OutDir => Path.Combine(AppContext.BaseDirectory, "data", "apf_test");
	static FileSizeTests()
	{
		Directory.CreateDirectory(OutDir);
	}

	static long EncodeAndGetSize(string pngName)
	{
		string pngPath = Path.Combine(DataDir, pngName);
		string apfPath = Path.Combine(OutDir, Path.ChangeExtension(pngName, ".apf"));

		using Image img = Image.FromFile(pngPath);
		ArbitraryPicture apf = new ArbitraryPicture(img);
		apf.Save(apfPath);

		return new FileInfo(apfPath).Length;
	}

	static long EstimateSerializedSize(int width, int height, Color[] pixels, Color background)
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
		for (int i = 0; i < pixels.Length; i++)
			if (pixels[i] != background)
				imageData[idx++] = pixels[i];

		var picture = new ArbitraryPicture(descriptor, background)
		{
			ImageData = imageData
		};
		ArbitraryPictureEncodingAnalysis analysis = picture.AnalyzeEncoding();
		return 1L + analysis.Stencil.SerializedSize + 4 + 4 + analysis.SelectedCandidate.PayloadSize;
	}

	static int GetMostCommonArgb(Color[] pixels)
	{
		var counts = new Dictionary<int, int>();
		foreach (Color pixel in pixels)
		{
			int argb = pixel.ToArgb();
			counts[argb] = counts.TryGetValue(argb, out int count) ? count + 1 : 1;
		}

		return counts
			.OrderByDescending(kvp => kvp.Value)
			.ThenBy(kvp => kvp.Key)
			.First()
			.Key;
	}

	static IEnumerable<Color[]> BuildSyntheticBackgroundCases(int width, int height)
	{
		for (int repeatedPixels = 2; repeatedPixels <= 20; repeatedPixels += 3)
		{
			Color[] pixels = new Color[width * height];
			for (int i = 0; i < pixels.Length; i++)
			{
				int r = (37 * i + 17) & 0xFF;
				int g = (91 * i + 53) & 0xFF;
				int b = (53 * i + 101) & 0xFF;
				pixels[i] = Color.FromArgb(255, r, g, b);
			}

			for (int i = 0; i < repeatedPixels; i++)
				pixels[(i * 3) % pixels.Length] = Color.White;
			yield return pixels;
		}
	}

	// v1.0 baselines (rANS entropy coding + LZ77 + Paeth + sub-byte palette) + ~2 KB headroom.

	[Fact]
	public void CircularImage_BelowSizeLimit()
	{
		long size = EncodeAndGetSize("circular_image.png");
		Assert.True(size <= 16_000,
			$"circular_image.apf is {size} bytes, expected <= 16000");
	}

	[Fact]
	public void Cow_BelowSizeLimit()
	{
		long size = EncodeAndGetSize("cow.png");
		Assert.True(size <= 443_000,
			$"cow.apf is {size} bytes, expected <= 443000");
	}

	[Fact]
	public void RotatedCow_BelowSizeLimit()
	{
		long size = EncodeAndGetSize("rotated_cow.png");
		Assert.True(size <= 206_000,
			$"rotated_cow.apf is {size} bytes, expected <= 206000");
	}

	[Fact]
	public void Sample_BelowSizeLimit()
	{
		long size = EncodeAndGetSize("sample.png");
		Assert.True(size <= 23_000,
			$"sample.apf is {size} bytes, expected <= 23000");
	}

	[Fact]
	public void Terminal_BelowSizeLimit()
	{
		long size = EncodeAndGetSize("terminal.png");
		Assert.True(size <= 38_000,
			$"terminal.apf is {size} bytes, expected <= 38000");
	}

	[Fact]
	public void AllImages_RoundTripLossless()
	{
		foreach (string png in Directory.GetFiles(DataDir, "*.png"))
		{
			string name = Path.GetFileNameWithoutExtension(png);

			using Image img = Image.FromFile(png);
			using Bitmap srcBmp = new Bitmap(img);

			ArbitraryPicture apf = new ArbitraryPicture(img);

			using MemoryStream ms = new MemoryStream();
			apf.Save(Path.Combine(OutDir, name + "_rt.apf"));

			ArbitraryPicture loaded = ArbitraryPicture.FromFile(
				Path.Combine(OutDir, name + "_rt.apf"));
			using Bitmap outBmp = loaded.ToBitmap(loaded.Background);

			Assert.Equal(srcBmp.Width, outBmp.Width);
			Assert.Equal(srcBmp.Height, outBmp.Height);

			for (int y = 0; y < srcBmp.Height; y++)
				for (int x = 0; x < srcBmp.Width; x++)
					Assert.Equal(srcBmp.GetPixel(x, y), outBmp.GetPixel(x, y));
		}
	}

	[Fact]
	public void BackgroundSelection_IsNeverWorseThanMostCommonHeuristic()
	{
		foreach (string png in Directory.GetFiles(DataDir, "*.png"))
		{
			using Image img = Image.FromFile(png);
			using Bitmap bmp = new Bitmap(img);
			Color[] pixels = new Color[bmp.Width * bmp.Height];
			for (int y = 0; y < bmp.Height; y++)
				for (int x = 0; x < bmp.Width; x++)
					pixels[y * bmp.Width + x] = bmp.GetPixel(x, y);

			int mostCommonArgb = GetMostCommonArgb(pixels);
			var picture = new ArbitraryPicture(img);
			long selectedSize = EstimateSerializedSize(bmp.Width, bmp.Height, pixels, picture.Background);
			long mostCommonSize = EstimateSerializedSize(bmp.Width, bmp.Height, pixels, Color.FromArgb(mostCommonArgb));

			Assert.True(
				selectedSize <= mostCommonSize,
				$"{Path.GetFileName(png)} picked background {picture.Background.ToArgb():X8} with estimated size {selectedSize}, expected <= most-common heuristic size {mostCommonSize}.");
		}

		foreach (Color[] pixels in BuildSyntheticBackgroundCases(8, 8))
		{
			int mostCommonArgb = GetMostCommonArgb(pixels);
			var picture = new ArbitraryPicture(8, 8, pixels);
			long selectedSize = EstimateSerializedSize(8, 8, pixels, picture.Background);
			long mostCommonSize = EstimateSerializedSize(8, 8, pixels, Color.FromArgb(mostCommonArgb));

			Assert.True(
				selectedSize <= mostCommonSize,
				$"Synthetic dense case picked background {picture.Background.ToArgb():X8} with estimated size {selectedSize}, expected <= most-common heuristic size {mostCommonSize}.");
		}
	}
}
