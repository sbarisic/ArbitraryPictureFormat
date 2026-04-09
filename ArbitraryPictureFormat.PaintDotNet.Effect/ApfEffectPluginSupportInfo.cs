using PaintDotNet;
using PaintDotNet.PropertySystem;
using System;
using System.Reflection;

namespace ArbitraryPictureFormat.PaintDotNet.Effect;

public class ApfEffectPluginSupportInfo : IPluginSupportInfo
{
	public string DisplayName => "APF Metadata Editor";
	public string Author => "ArbitraryPictureFormat";
	public string Copyright => $"Copyright © {DateTime.Now.Year}";
	public Version Version => Assembly.GetExecutingAssembly().GetName().Version!;
	public Uri WebsiteUri => new("https://github.com");
}
