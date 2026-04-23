using System.Drawing;
using ArbitraryPictureFormat;

namespace ArbitraryPictureFormat.Tests;

public class DeserializationValidationTests
{
	[Fact]
	public void Deserialize_RejectsNegativeImageCount()
	{
		using var ms = new MemoryStream();
		using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
		{
			writer.Write((byte)0x20);
			writer.Write(-1);
		}

		ms.Position = 0;
		Assert.Throws<InvalidDataException>(() => ApfFile.Deserialize(ms));
	}

	[Fact]
	public void Deserialize_RejectsNegativeMetadataCount()
	{
		using var ms = new MemoryStream();
		using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
		{
			writer.Write((byte)0x11);
			writer.Write(-1);
		}

		ms.Position = 0;
		Assert.Throws<InvalidDataException>(() => ApfFile.Deserialize(ms));
	}

	[Fact]
	public void Deserialize_RejectsTruncatedName()
	{
		using var ms = new MemoryStream();
		using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
		{
			writer.Write((byte)0x20);
			writer.Write(1);
			writer.Write(8);
			writer.Write(new byte[] { (byte)'l', (byte)'a' });
		}

		ms.Position = 0;
		Assert.Throws<InvalidDataException>(() => ApfFile.Deserialize(ms));
	}

	[Fact]
	public void Deserialize_RejectsNegativeDimensions()
	{
		using var ms = new MemoryStream();
		using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
		{
			writer.Write((byte)0x10);
			writer.Write(-1);
			writer.Write(1);
			writer.Write(0);
			writer.Write(0);
		}

		ms.Position = 0;
		Assert.Throws<InvalidDataException>(() => ApfFile.Deserialize(ms));
	}

	[Fact]
	public void Deserialize_RejectsPixelCountMismatch()
	{
		using var ms = new MemoryStream();
		using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
		{
			WriteFullShapeV10Header(writer, width: 1, height: 1);
			writer.Write(Color.Transparent.ToArgb());
			writer.Write(0);
		}

		ms.Position = 0;
		Assert.Throws<InvalidDataException>(() => ApfFile.Deserialize(ms));
	}

	[Fact]
	public void Deserialize_RejectsTruncatedShapeBytes()
	{
		using var ms = new MemoryStream();
		using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
		{
			writer.Write((byte)0x10);
			writer.Write(8);
			writer.Write(1);
			writer.Write(1);
			writer.Write(4);
			writer.Write(new byte[] { 0, 1 });
		}

		ms.Position = 0;
		Assert.Throws<InvalidDataException>(() => ApfFile.Deserialize(ms));
	}

	[Fact]
	public void Deserialize_RejectsUnderfilledCompressedPlane()
	{
		using var ms = new MemoryStream();
		using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
		{
			WriteFullShapeV10Header(writer, width: 1, height: 1);
			writer.Write(Color.Transparent.ToArgb());
			writer.Write(1);
			writer.Write((byte)PixelEncoding.ChannelPlanes);
			writer.Write((byte)2);
			writer.Write((byte)0);
			writer.Write((byte)0);
			writer.Write((byte)0);
			writer.Write((byte)255);
			writer.Write(1);
			writer.Write((byte)0);
		}

		ms.Position = 0;
		Assert.Throws<InvalidDataException>(() => ApfFile.Deserialize(ms));
	}

	[Fact]
	public void Deserialize_RejectsPaletteIndexOutsidePalette()
	{
		using var ms = new MemoryStream();
		using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
		{
			WriteFullShapeV10Header(writer, width: 1, height: 1);
			writer.Write(Color.Transparent.ToArgb());
			writer.Write(1);
			writer.Write((byte)PixelEncoding.PaletteIndexed);
			writer.Write((ushort)1);
			writer.Write(Color.Red.ToArgb());
			writer.Write((byte)8);

			byte[] compressed = Helpers.Compress(new byte[] { 1 });
			writer.Write(compressed.Length);
			writer.Write(compressed);
		}

		ms.Position = 0;
		Assert.Throws<InvalidDataException>(() => ApfFile.Deserialize(ms));
	}

	static void WriteFullShapeV10Header(BinaryWriter writer, int width, int height)
	{
		writer.Write((byte)0x10);
		writer.Write(width);
		writer.Write(height);
		writer.Write(0);
		writer.Write(0);
	}
}
