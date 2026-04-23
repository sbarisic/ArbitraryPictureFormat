using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.Imaging;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;

namespace ArbitraryPictureFormat.PaintDotNet.Effect;

[PluginSupportInfo<ApfEffectPluginSupportInfo>]
public class ApfMetadataEffect : PropertyBasedBitmapEffect
{
	private enum PropertyNames
	{
		Metadata
	}

	private int _layerIndex = -1;
	private string _layerName = "";

	public ApfMetadataEffect()
		: base(
			"Edit .apf",
			null,
			BitmapEffectOptions.Create() with { IsConfigurable = true })
	{
	}

	private string GetCurrentLayerName()
	{
		_layerIndex = -1;
		try
		{
			var env = ((IEffect)this).Environment;
			int idx = env.SourceLayerIndex;
			var layers = env.Document.Layers;
			if (idx >= 0 && idx < layers.Count)
			{
				_layerIndex = idx;
				return layers[idx].Name ?? "";
			}
		}
		catch { }
		return "";
	}

	protected override PropertyCollection OnCreatePropertyCollection()
	{
		_layerName = GetCurrentLayerName();
		string current = ApfMetadataStore.Serialize(_layerIndex, _layerName);
		return new PropertyCollection(
		[
			new StringProperty(PropertyNames.Metadata, current, 32767)
		]);
	}

	protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
	{
		ControlInfo info = ControlInfo.CreateDefaultConfigUI(props);
		info.SetPropertyControlValue(
			PropertyNames.Metadata,
			ControlInfoPropertyNames.DisplayName,
			$"Metadata for \"{_layerName}\" (key=value per line)");
		info.SetPropertyControlValue(
			PropertyNames.Metadata,
			ControlInfoPropertyNames.Multiline,
			true);
		return info;
	}

	protected override void OnCustomizeConfigUIWindowProperties(PropertyCollection props)
	{
		base.OnCustomizeConfigUIWindowProperties(props);
		props[ControlInfoPropertyNames.WindowTitle].Value = "Edit APF Metadata";
	}

	protected override void OnSetToken(PropertyBasedEffectConfigToken newToken)
	{
		string text = newToken.GetProperty<StringProperty>(PropertyNames.Metadata).Value;
		ApfMetadataStore.SetLayer(_layerIndex, ApfMetadataStore.Parse(text));
		base.OnSetToken(newToken);
	}

	protected override void OnRender(IBitmapEffectOutput output)
	{
		using IEffectInputBitmap<ColorBgra32> sourceBitmap = Environment.GetSourceBitmapBgra32();
		using IBitmapLock<ColorBgra32> dst = output.LockBgra32();
		using IBitmapLock<ColorBgra32> src = sourceBitmap.Lock(output.Bounds);

		src.AsRegionPtr().CopyTo(dst.AsRegionPtr());
	}
}
