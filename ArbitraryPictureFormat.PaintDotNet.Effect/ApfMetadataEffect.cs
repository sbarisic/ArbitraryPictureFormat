using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using System.Drawing;

namespace ArbitraryPictureFormat.PaintDotNet.Effect;

[PluginSupportInfo<ApfEffectPluginSupportInfo>]
public class ApfMetadataEffect : PropertyBasedEffect
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
			(Image)null,
			null,
			new EffectOptions { Flags = EffectFlags.Configurable })
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

	protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
	{
		base.OnSetRenderInfo(newToken, dstArgs, srcArgs);

		string text = newToken.GetProperty<StringProperty>(PropertyNames.Metadata).Value;
		ApfMetadataStore.SetLayer(_layerIndex, ApfMetadataStore.Parse(text));
	}

	protected override void OnRender(Rectangle[] renderRects, int startIndex, int length)
	{
		// No pixel changes — copy source to destination unchanged
		for (int i = startIndex; i < startIndex + length; i++)
			DstArgs.Surface.CopySurface(SrcArgs.Surface, renderRects[i].Location, renderRects[i]);
	}
}
