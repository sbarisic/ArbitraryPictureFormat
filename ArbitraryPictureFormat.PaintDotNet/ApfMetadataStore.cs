using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ArbitraryPictureFormat.PaintDotNet;

/// <summary>
/// Shared in-process store for per-layer APF metadata.
/// Uses a temp file so the FileType (in FileTypes/) and the Effect (in Effects/)
/// share the same data despite being loaded as separate assemblies.
/// </summary>
internal static class ApfMetadataStore
{
	private static string FilePath =>
		Path.Combine(Path.GetTempPath(), $"apf-metadata-{Environment.ProcessId}.ini");

	private static Dictionary<string, Dictionary<string, string>> ReadAll()
	{
		var result = new Dictionary<string, Dictionary<string, string>>();
		string path = FilePath;
		if (!File.Exists(path))
			return result;

		try
		{
			string[] lines = File.ReadAllLines(path);
			string currentLayer = "";
			foreach (string raw in lines)
			{
				string line = raw.Trim();
				if (line.StartsWith("[") && line.EndsWith("]"))
				{
					currentLayer = line.Substring(1, line.Length - 2);
					if (!result.ContainsKey(currentLayer))
						result[currentLayer] = new Dictionary<string, string>();
				}
				else if (!string.IsNullOrEmpty(line))
				{
					int eq = line.IndexOf('=');
					if (eq > 0)
					{
						string key = line.Substring(0, eq).Trim();
						string value = line.Substring(eq + 1).Trim();
						if (key.Length > 0)
						{
							if (!result.ContainsKey(currentLayer))
								result[currentLayer] = new Dictionary<string, string>();
							result[currentLayer][key] = value;
						}
					}
				}
			}
		}
		catch { }
		return result;
	}

	private static void WriteAll(Dictionary<string, Dictionary<string, string>> data)
	{
		try
		{
			var sb = new StringBuilder();
			foreach (var layer in data)
			{
				if (layer.Value.Count == 0)
					continue;
				sb.AppendLine($"[{layer.Key}]");
				foreach (var kvp in layer.Value)
					sb.AppendLine($"{kvp.Key}={kvp.Value}");
				sb.AppendLine();
			}
			File.WriteAllText(FilePath, sb.ToString());
		}
		catch { }
	}

	public static Dictionary<string, string> GetLayer(string layerName)
	{
		var all = ReadAll();
		if (all.TryGetValue(layerName ?? "", out var meta))
			return new Dictionary<string, string>(meta);
		return new Dictionary<string, string>();
	}

	public static Dictionary<string, string> GetLayer(int layerIndex, string fallbackLayerName)
	{
		var all = ReadAll();
		if (layerIndex >= 0 && all.TryGetValue(GetLayerKey(layerIndex), out var meta))
			return new Dictionary<string, string>(meta);
		if (all.TryGetValue(fallbackLayerName ?? "", out meta))
			return new Dictionary<string, string>(meta);
		return new Dictionary<string, string>();
	}

	public static void SetLayer(string layerName, Dictionary<string, string> metadata)
	{
		var all = ReadAll();
		all[layerName ?? ""] = metadata != null ? new Dictionary<string, string>(metadata) : new();
		WriteAll(all);
	}

	public static void SetLayer(int layerIndex, Dictionary<string, string> metadata)
	{
		var all = ReadAll();
		all[GetLayerKey(layerIndex)] = metadata != null ? new Dictionary<string, string>(metadata) : new();
		WriteAll(all);
	}

	public static void Clear()
	{
		try { File.Delete(FilePath); } catch { }
	}

	public static string Serialize(string layerName)
	{
		var meta = GetLayer(layerName);
		return SerializeMetadata(meta);
	}

	public static string Serialize(int layerIndex, string fallbackLayerName)
	{
		var meta = GetLayer(layerIndex, fallbackLayerName);
		return SerializeMetadata(meta);
	}

	private static string SerializeMetadata(Dictionary<string, string> meta)
	{
		if (meta.Count == 0)
			return "";
		var sb = new StringBuilder();
		foreach (var kvp in meta)
			sb.AppendLine($"{kvp.Key}={kvp.Value}");
		return sb.ToString().TrimEnd();
	}

	private static string GetLayerKey(int layerIndex)
	{
		return layerIndex >= 0 ? "index:" + layerIndex.ToString() : "";
	}

	public static Dictionary<string, string> Parse(string text)
	{
		var result = new Dictionary<string, string>();
		if (string.IsNullOrWhiteSpace(text))
			return result;

		foreach (string line in text.Split('\n'))
		{
			string trimmed = line.Trim('\r', ' ', '\t');
			if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
				continue;
			int eq = trimmed.IndexOf('=');
			if (eq > 0)
			{
				string key = trimmed.Substring(0, eq).Trim();
				string value = trimmed.Substring(eq + 1).Trim();
				if (key.Length > 0)
					result[key] = value;
			}
		}
		return result;
	}
}
