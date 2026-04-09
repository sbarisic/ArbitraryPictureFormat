using PaintDotNet;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using System;
using System.Drawing;
using System.IO;

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
				SupportsLayers = false
			})
	{
	}

	protected override Document OnLoad(Stream input)
	{
		var apf = new ArbitraryPicture(input);

		int width = apf.Descriptor.Width;
		int height = apf.Descriptor.Height;

		var doc = new Document(width, height);
		var layer = new BitmapLayer(width, height);
		Surface surface = layer.Surface;

		int imgIdx = 0;
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				Color c;
				if (apf.Descriptor.Get(x, y))
					c = apf.ImageData[imgIdx++];
				else
					c = apf.Background;

				surface[x, y] = ColorBgra.FromBgra(c.B, c.G, c.R, c.A);
			}
		}

		doc.Layers.Add(layer);
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
		input.Flatten(scratchSurface);

		int width = scratchSurface.Width;
		int height = scratchSurface.Height;

		var pixels = new Color[width * height];
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				ColorBgra cb = scratchSurface[x, y];
				pixels[y * width + x] = Color.FromArgb(cb.A, cb.R, cb.G, cb.B);
			}
		}

		var apf = new ArbitraryPicture(width, height, pixels);

		PixelEncoding? forced = null;
		var choice = (StaticListChoiceProperty)token.GetProperty(PropertyNames.EncodingStrategy);
		string selected = (string)choice.Value;
		if (selected != "Auto" && Enum.TryParse<PixelEncoding>(selected, out var enc))
			forced = enc;

		apf.Serialize(output, forced);
	}
}
