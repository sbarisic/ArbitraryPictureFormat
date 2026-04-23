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
}
