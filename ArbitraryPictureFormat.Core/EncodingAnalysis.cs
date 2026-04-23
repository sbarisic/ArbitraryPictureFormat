using System.Collections.Generic;
using System.Drawing;

namespace ArbitraryPictureFormat
{
	public enum CompressionMode : byte
	{
		Rle = 0,
		Lz77 = 1,
		Rans = 2,
		Lz77Rans = 3,
	}

	public enum StencilEncodingMode : byte
	{
		FullCoverage = 0,
		ZOrder = 1,
		InvertedZOrder = 2,
		Scanline = 3,
		InvertedScanline = 4,
	}

	public sealed class CompressionAnalysis
	{
		public CompressionAnalysis(CompressionMode mode, int rawSize, int encodedSize)
		{
			Mode = mode;
			RawSize = rawSize;
			EncodedSize = encodedSize;
		}

		public CompressionMode Mode { get; }
		public int RawSize { get; }
		public int EncodedSize { get; }
	}

	public sealed class PayloadComponentAnalysis
	{
		public PayloadComponentAnalysis(string name, int rawSize, int storedSize, string transform = null, CompressionAnalysis compression = null, string details = null)
		{
			Name = name;
			RawSize = rawSize;
			StoredSize = storedSize;
			Transform = transform;
			Compression = compression;
			Details = details;
		}

		public string Name { get; }
		public int RawSize { get; }
		public int StoredSize { get; }
		public string Transform { get; }
		public CompressionAnalysis Compression { get; }
		public string Details { get; }
	}

	public sealed class PixelEncodingCandidateAnalysis
	{
		public PixelEncodingCandidateAnalysis(PixelEncoding mode, int payloadSize, bool selected, IReadOnlyList<PayloadComponentAnalysis> components)
		{
			Mode = mode;
			PayloadSize = payloadSize;
			Selected = selected;
			Components = components;
		}

		public PixelEncoding Mode { get; }
		public int PayloadSize { get; }
		public bool Selected { get; }
		public IReadOnlyList<PayloadComponentAnalysis> Components { get; }
	}

	public sealed class StencilEncodingAnalysis
	{
		public StencilEncodingAnalysis(StencilEncodingMode mode, bool isFullCoverage, int rawSize, int serializedSize, CompressionAnalysis compression = null)
		{
			Mode = mode;
			IsFullCoverage = isFullCoverage;
			RawSize = rawSize;
			SerializedSize = serializedSize;
			Compression = compression;
		}

		public StencilEncodingMode Mode { get; }
		public bool IsFullCoverage { get; }
		public int RawSize { get; }
		public int SerializedSize { get; }
		public CompressionAnalysis Compression { get; }
	}

	public sealed class ArbitraryPictureEncodingAnalysis
	{
		public ArbitraryPictureEncodingAnalysis(Color background, int totalPixelCount, int shapePixelCount, StencilEncodingAnalysis stencil, IReadOnlyList<PixelEncodingCandidateAnalysis> candidates, PixelEncodingCandidateAnalysis selectedCandidate)
		{
			Background = background;
			TotalPixelCount = totalPixelCount;
			ShapePixelCount = shapePixelCount;
			Stencil = stencil;
			Candidates = candidates;
			SelectedCandidate = selectedCandidate;
		}

		public Color Background { get; }
		public int TotalPixelCount { get; }
		public int ShapePixelCount { get; }
		public StencilEncodingAnalysis Stencil { get; }
		public IReadOnlyList<PixelEncodingCandidateAnalysis> Candidates { get; }
		public PixelEncodingCandidateAnalysis SelectedCandidate { get; }
	}
}
