using System.Collections.Generic;
using System.Drawing;
using ArbitraryPictureFormat;

namespace ArbitraryPictureFormat.Tests;

public class ApfFileTests
{
	static string DataDir => Path.Combine(AppContext.BaseDirectory, "data", "png");

	[Fact]
	public void SingleImage_NoMetadata_WritesV10_RoundTrips()
	{
		// A single image with no metadata should produce a v1.0 file
		using Image img = Image.FromFile(Path.Combine(DataDir, "sample.png"));
		var pic = new ArbitraryPicture(img);
		var file = ApfFile.FromSingleImage(pic);

		using var ms = new MemoryStream();
		file.Serialize(ms);

		// Check version byte is 0x10
		byte[] data = ms.ToArray();
		Assert.Equal(0x10, data[0]);

		// Round-trip
		ms.Position = 0;
		ApfFile loaded = ApfFile.Deserialize(ms);
		Assert.Single(loaded.Images);
		Assert.Empty(loaded.Images[0].Name);
		Assert.Empty(loaded.Images[0].Metadata);
		Assert.Equal(pic.Descriptor.Width, loaded.Images[0].Picture.Descriptor.Width);
		Assert.Equal(pic.Descriptor.Height, loaded.Images[0].Picture.Descriptor.Height);
	}

	[Fact]
	public void SingleImage_WithMetadata_WritesV11_RoundTrips()
	{
		// A single image with metadata should produce a v1.1 file
		var pixels = new Color[4 * 4];
		for (int i = 0; i < pixels.Length; i++)
			pixels[i] = Color.FromArgb(255, i * 16, i * 8, i * 4);

		var pic = new ArbitraryPicture(4, 4, pixels);
		var metadata = new Dictionary<string, string>
		{
			["author"] = "Test User",
			["description"] = "A test image with unicode: 日本語",
			["version"] = "1.0"
		};
		var file = ApfFile.FromSingleImage(pic, "", metadata);

		using var ms = new MemoryStream();
		file.Serialize(ms);

		// Check version byte is 0x11
		byte[] data = ms.ToArray();
		Assert.Equal(0x11, data[0]);

		// Round-trip
		ms.Position = 0;
		ApfFile loaded = ApfFile.Deserialize(ms);
		Assert.Single(loaded.Images);
		Assert.Equal(3, loaded.Images[0].Metadata.Count);
		Assert.Equal("Test User", loaded.Images[0].Metadata["author"]);
		Assert.Equal("A test image with unicode: 日本語", loaded.Images[0].Metadata["description"]);
		Assert.Equal("1.0", loaded.Images[0].Metadata["version"]);

		// Verify pixel data
		ArbitraryPicture loadedPic = loaded.Images[0].Picture;
		Assert.Equal(4, loadedPic.Descriptor.Width);
		Assert.Equal(4, loadedPic.Descriptor.Height);
	}

	[Fact]
	public void MultiImage_WritesV20_RoundTrips()
	{
		// Multiple images should produce a v2.0 file
		var pixels1 = new Color[3 * 3];
		for (int i = 0; i < pixels1.Length; i++)
			pixels1[i] = Color.FromArgb(255, 100, 0, 0);

		var pixels2 = new Color[5 * 5];
		for (int i = 0; i < pixels2.Length; i++)
			pixels2[i] = Color.FromArgb(255, 0, 100, 0);

		var pic1 = new ArbitraryPicture(3, 3, pixels1);
		var pic2 = new ArbitraryPicture(5, 5, pixels2);

		var file = new ApfFile();
		file.Images.Add(new ApfImage(pic1, "diffuse"));
		file.Images.Add(new ApfImage(pic2, "normal", new Dictionary<string, string> { ["type"] = "normalmap" }));

		using var ms = new MemoryStream();
		file.Serialize(ms);

		// Check version byte is 0x20
		byte[] data = ms.ToArray();
		Assert.Equal(0x20, data[0]);

		// Round-trip
		ms.Position = 0;
		ApfFile loaded = ApfFile.Deserialize(ms);
		Assert.Equal(2, loaded.Images.Count);

		Assert.Equal("diffuse", loaded.Images[0].Name);
		Assert.Empty(loaded.Images[0].Metadata);
		Assert.Equal(3, loaded.Images[0].Picture.Descriptor.Width);
		Assert.Equal(3, loaded.Images[0].Picture.Descriptor.Height);

		Assert.Equal("normal", loaded.Images[1].Name);
		Assert.Single(loaded.Images[1].Metadata);
		Assert.Equal("normalmap", loaded.Images[1].Metadata["type"]);
		Assert.Equal(5, loaded.Images[1].Picture.Descriptor.Width);
		Assert.Equal(5, loaded.Images[1].Picture.Descriptor.Height);
	}

	[Fact]
	public void MultiImage_MixedSubVersions_RoundTrips()
	{
		// v2.0 with one v1.0 sub-image (no metadata) and one v1.1 sub-image (with metadata)
		var pixels = new Color[2 * 2];
		for (int i = 0; i < pixels.Length; i++)
			pixels[i] = Color.Red;

		var pic1 = new ArbitraryPicture(2, 2, pixels);
		var pic2 = new ArbitraryPicture(2, 2, pixels);

		var file = new ApfFile();
		file.Images.Add(new ApfImage(pic1, "layer1")); // no metadata → v1.0 sub
		file.Images.Add(new ApfImage(pic2, "layer2", new Dictionary<string, string> { ["key"] = "value" })); // has metadata → v1.1 sub

		using var ms = new MemoryStream();
		file.Serialize(ms);

		ms.Position = 0;
		ApfFile loaded = ApfFile.Deserialize(ms);
		Assert.Equal(2, loaded.Images.Count);
		Assert.False(loaded.Images[0].HasMetadata);
		Assert.True(loaded.Images[1].HasMetadata);
		Assert.Equal("value", loaded.Images[1].Metadata["key"]);
	}

	[Fact]
	public void V10_Compat_ExistingFiles_LoadThroughApfFile()
	{
		// Existing v1.0 files produced by ArbitraryPicture.Save should load through ApfFile
		foreach (string png in Directory.GetFiles(DataDir, "*.png"))
		{
			string name = Path.GetFileNameWithoutExtension(png);

			using Image img = Image.FromFile(png);
			var apf = new ArbitraryPicture(img);

			// Write using old v1.0 path (ArbitraryPicture.Serialize)
			using var ms = new MemoryStream();
			apf.Serialize(ms);

			// Load through ApfFile
			ms.Position = 0;
			ApfFile loaded = ApfFile.Deserialize(ms);
			Assert.Single(loaded.Images);
			Assert.Empty(loaded.Images[0].Name);
			Assert.Empty(loaded.Images[0].Metadata);
			Assert.Equal(apf.Descriptor.Width, loaded.Images[0].Picture.Descriptor.Width);
			Assert.Equal(apf.Descriptor.Height, loaded.Images[0].Picture.Descriptor.Height);
			Assert.Equal(apf.ImageData.Length, loaded.Images[0].Picture.ImageData.Length);
		}
	}

	[Fact]
	public void GetImage_ByName_ReturnsCorrectImage()
	{
		var pixels = new Color[] { Color.Red, Color.Blue, Color.Green, Color.White };
		var pic1 = new ArbitraryPicture(2, 2, pixels);
		var pic2 = new ArbitraryPicture(2, 2, pixels);

		var file = new ApfFile();
		file.Images.Add(new ApfImage(pic1, "diffuse"));
		file.Images.Add(new ApfImage(pic2, "normal"));

		Assert.Equal("diffuse", file.GetImage("diffuse").Name);
		Assert.Equal("normal", file.GetImage("normal").Name);
		Assert.Equal("diffuse", file.GetImage("").Name); // default → first
		Assert.Equal("diffuse", file.GetImage(null).Name); // null → first
		Assert.Null(file.GetImage("nonexistent"));
	}

	[Fact]
	public void MultiImage_PixelData_Lossless()
	{
		// Verify actual pixel data survives round-trip in multi-image
		var pixels1 = new Color[4 * 4];
		var pixels2 = new Color[4 * 4];

		for (int i = 0; i < 16; i++)
		{
			pixels1[i] = Color.FromArgb(255, i * 16, 255 - i * 16, i * 8);
			pixels2[i] = Color.FromArgb(128, i * 8, i * 4, i * 16);
		}

		var pic1 = new ArbitraryPicture(4, 4, pixels1);
		var pic2 = new ArbitraryPicture(4, 4, pixels2);

		var file = new ApfFile();
		file.Images.Add(new ApfImage(pic1, "img1"));
		file.Images.Add(new ApfImage(pic2, "img2"));

		using var ms = new MemoryStream();
		file.Serialize(ms);

		ms.Position = 0;
		ApfFile loaded = ApfFile.Deserialize(ms);

		// Verify by converting back to full grid
		using Bitmap bmp1 = loaded.Images[0].Picture.ToBitmap(loaded.Images[0].Picture.Background);
		using Bitmap bmp2 = loaded.Images[1].Picture.ToBitmap(loaded.Images[1].Picture.Background);

		for (int y = 0; y < 4; y++)
			for (int x = 0; x < 4; x++)
			{
				Assert.Equal(pixels1[y * 4 + x], bmp1.GetPixel(x, y));
				Assert.Equal(pixels2[y * 4 + x], bmp2.GetPixel(x, y));
			}
	}
}
