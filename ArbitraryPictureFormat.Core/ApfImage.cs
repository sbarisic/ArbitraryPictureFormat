using System.Collections.Generic;

namespace ArbitraryPictureFormat
{
	public class ApfImage
	{
		public string Name { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
		public ArbitraryPicture Picture { get; set; }

		public ApfImage(ArbitraryPicture picture, string name = "", Dictionary<string, string> metadata = null)
		{
			Picture = picture;
			Name = name ?? "";
			Metadata = metadata ?? new Dictionary<string, string>();
		}

		public bool HasMetadata => Metadata.Count > 0;
	}
}
