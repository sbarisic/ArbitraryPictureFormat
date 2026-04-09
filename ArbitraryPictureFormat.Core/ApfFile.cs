using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ArbitraryPictureFormat
{
	public class ApfFile
	{
		const byte VERSION_1_0 = 0x10;
		const byte VERSION_1_1 = 0x11;
		const byte VERSION_2_0 = 0x20;

		public List<ApfImage> Images { get; set; } = new List<ApfImage>();

		public ApfFile() { }

		public ApfFile(IEnumerable<ApfImage> images)
		{
			Images = new List<ApfImage>(images);
		}

		public static ApfFile FromSingleImage(ArbitraryPicture picture, string name = "", Dictionary<string, string> metadata = null)
		{
			var file = new ApfFile();
			file.Images.Add(new ApfImage(picture, name, metadata));
			return file;
		}

		public ApfImage GetImage(string name)
		{
			if (string.IsNullOrEmpty(name))
				return Images.Count > 0 ? Images[0] : null;

			return Images.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
				?? (Images.Count > 0 ? Images[0] : null);
		}

		public void Serialize(Stream S, PixelEncoding? forcedEncoding = null)
		{
			using (BinaryWriter Writer = new BinaryWriter(S, Encoding.UTF8, true))
			{
				if (Images.Count == 1 && !Images[0].HasMetadata)
				{
					// v1.0: single image, no metadata
					Writer.Write(VERSION_1_0);
					Images[0].Picture.SerializePayload(Writer, forcedEncoding);
				}
				else if (Images.Count == 1)
				{
					// v1.1: single image with metadata
					Writer.Write(VERSION_1_1);
					WriteMetadata(Writer, Images[0].Metadata);
					Images[0].Picture.SerializePayload(Writer, forcedEncoding);
				}
				else
				{
					// v2.0: multiple images
					Writer.Write(VERSION_2_0);
					Writer.Write(Images.Count);

					foreach (var image in Images)
					{
						byte[] nameBytes = Encoding.UTF8.GetBytes(image.Name ?? "");
						Writer.Write(nameBytes.Length);
						if (nameBytes.Length > 0)
							Writer.Write(nameBytes);

						byte subVersion = image.HasMetadata ? VERSION_1_1 : VERSION_1_0;
						Writer.Write(subVersion);

						if (image.HasMetadata)
							WriteMetadata(Writer, image.Metadata);

						image.Picture.SerializePayload(Writer, forcedEncoding);
					}
				}
			}
		}

		public static ApfFile Deserialize(Stream S)
		{
			var file = new ApfFile();
			using (BinaryReader Reader = new BinaryReader(S, Encoding.UTF8, true))
			{
				byte version = Reader.ReadByte();

				switch (version)
				{
					case VERSION_1_0:
					{
						var pic = new ArbitraryPicture(new ShapeDesc(0, 0), System.Drawing.Color.Transparent);
						pic.DeserializePayload(Reader);
						file.Images.Add(new ApfImage(pic));
						break;
					}

					case VERSION_1_1:
					{
						var metadata = ReadMetadata(Reader);
						var pic = new ArbitraryPicture(new ShapeDesc(0, 0), System.Drawing.Color.Transparent);
						pic.DeserializePayload(Reader);
						file.Images.Add(new ApfImage(pic, "", metadata));
						break;
					}

					case VERSION_2_0:
					{
						int imageCount = Reader.ReadInt32();
						for (int i = 0; i < imageCount; i++)
						{
							int nameLen = Reader.ReadInt32();
							string name = nameLen > 0
								? Encoding.UTF8.GetString(Reader.ReadBytes(nameLen))
								: "";

							byte subVersion = Reader.ReadByte();
							Dictionary<string, string> metadata = null;

							if (subVersion == VERSION_1_1)
								metadata = ReadMetadata(Reader);
							else if (subVersion != VERSION_1_0)
								throw new InvalidDataException("Unknown sub-image version: 0x" + subVersion.ToString("X2"));

							var pic = new ArbitraryPicture(new ShapeDesc(0, 0), System.Drawing.Color.Transparent);
							pic.DeserializePayload(Reader);
							file.Images.Add(new ApfImage(pic, name, metadata));
						}
						break;
					}

					default:
						throw new InvalidDataException("Unknown APF format version: 0x" + version.ToString("X2"));
				}
			}
			return file;
		}

		public static ApfFile FromFile(string filePath)
		{
			using (FileStream fs = File.OpenRead(filePath))
				return Deserialize(fs);
		}

		public void Save(string filePath, PixelEncoding? forcedEncoding = null)
		{
			using (FileStream fs = File.Create(filePath))
				Serialize(fs, forcedEncoding);
		}

		static void WriteMetadata(BinaryWriter writer, Dictionary<string, string> metadata)
		{
			writer.Write(metadata.Count);
			foreach (var kvp in metadata)
			{
				byte[] keyBytes = Encoding.UTF8.GetBytes(kvp.Key);
				writer.Write(keyBytes.Length);
				writer.Write(keyBytes);

				byte[] valBytes = Encoding.UTF8.GetBytes(kvp.Value);
				writer.Write(valBytes.Length);
				writer.Write(valBytes);
			}
		}

		static Dictionary<string, string> ReadMetadata(BinaryReader reader)
		{
			int count = reader.ReadInt32();
			var metadata = new Dictionary<string, string>(count);
			for (int i = 0; i < count; i++)
			{
				int keyLen = reader.ReadInt32();
				string key = Encoding.UTF8.GetString(reader.ReadBytes(keyLen));

				int valLen = reader.ReadInt32();
				string value = Encoding.UTF8.GetString(reader.ReadBytes(valLen));

				metadata[key] = value;
			}
			return metadata;
		}
	}
}
