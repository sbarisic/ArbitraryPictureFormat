using PaintDotNet;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace ArbitraryPictureFormat.PaintDotNet;

[PluginSupportInfo<ApfPluginSupportInfo>]
public class ApfFileType : PropertyBasedFileType
{
	private enum PropertyNames
	{
		EncodingStrategy
	}

	private static readonly string[] EncodingChoices =
	[
		"Auto",
		"ChannelPlanes",
		"PaletteIndexed",
		"ColorSorted",
		"SolidFill",
		"MonoAlpha",
		"PaethFullGrid"
	];

	public ApfFileType()
		: base(
			"Arbitrary Picture Format",
			new FileTypeOptions
			{
				LoadExtensions = [".apf"],
				SaveExtensions = [".apf"],
				SupportsLayers = true
			})
	{
	}

	protected override Document OnLoad(Stream input)
	{
		ApfFile apfFile = ApfFile.Deserialize(input);

		if (apfFile.Images.Count == 0)
			throw new InvalidDataException("APF file contains no images");

		int canvasW = apfFile.Images.Max(i => i.Picture.Descriptor.Width);
		int canvasH = apfFile.Images.Max(i => i.Picture.Descriptor.Height);

		var doc = new Document(canvasW, canvasH);

		ApfMetadataStore.Clear();

		foreach (ApfImage apfImage in apfFile.Images)
		{
			var layer = new BitmapLayer(canvasW, canvasH);

			if (!string.IsNullOrEmpty(apfImage.Name))
				layer.Name = apfImage.Name;

			// Store metadata keyed by the actual layer name (which may be
			// a Paint.NET default like "Background" if the APF name was empty)
			ApfMetadataStore.SetLayer(layer.Name,
				apfImage.HasMetadata ? apfImage.Metadata : new Dictionary<string, string>());

			Surface surface = layer.Surface;
			ArbitraryPicture pic = apfImage.Picture;
			int imgW = pic.Descriptor.Width;
			int imgH = pic.Descriptor.Height;

			int offsetX = (canvasW - imgW) / 2;
			int offsetY = (canvasH - imgH) / 2;

			int imgIdx = 0;
			for (int y = 0; y < imgH; y++)
			{
				for (int x = 0; x < imgW; x++)
				{
					Color c;
					if (pic.Descriptor.Get(x, y))
						c = pic.ImageData[imgIdx++];
					else
						c = pic.Background;

					surface[offsetX + x, offsetY + y] = ColorBgra.FromBgra(c.B, c.G, c.R, c.A);
				}
			}

			doc.Layers.Add(layer);
		}

		return doc;
	}

	public override PropertyCollection OnCreateSavePropertyCollection()
	{
		return new PropertyCollection(
		[
			new StaticListChoiceProperty(PropertyNames.EncodingStrategy, EncodingChoices, 0)
		]);
	}

	public override ControlInfo OnCreateSaveConfigUI(PropertyCollection props)
	{
		ControlInfo info = ControlInfo.CreateDefaultConfigUI(props);
		info.SetPropertyControlValue(
			PropertyNames.EncodingStrategy,
			ControlInfoPropertyNames.DisplayName,
			"Encoding Strategy");
		return info;
	}

	protected override void OnSaveT(
		Document input,
		Stream output,
		PropertyBasedSaveConfigToken token,
		Surface scratchSurface,
		ProgressEventHandler progressCallback)
	{
		PixelEncoding? forced = null;
		var choice = (StaticListChoiceProperty)token.GetProperty(PropertyNames.EncodingStrategy);
		string selected = (string)choice.Value;
		if (selected != "Auto" && Enum.TryParse<PixelEncoding>(selected, out var enc))
			forced = enc;

		var apfFile = new ApfFile();

		foreach (Layer layer in input.Layers)
		{
			if (layer is not BitmapLayer bitmapLayer)
				continue;

			Surface layerSurface = bitmapLayer.Surface;
			int width = layerSurface.Width;
			int height = layerSurface.Height;

			var pixels = new Color[width * height];
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					ColorBgra cb = layerSurface[x, y];
					pixels[y * width + x] = Color.FromArgb(cb.A, cb.R, cb.G, cb.B);
				}
			}

			var pic = new ArbitraryPicture(width, height, pixels);
			string name = layer.Name ?? "";

			var layerMeta = ApfMetadataStore.GetLayer(name);
			apfFile.Images.Add(new ApfImage(pic, name, layerMeta.Count > 0 ? layerMeta : null));
		}

		apfFile.Serialize(output, forced);
	}
}
