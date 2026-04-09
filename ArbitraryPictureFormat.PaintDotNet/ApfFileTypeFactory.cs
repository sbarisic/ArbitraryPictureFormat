using PaintDotNet;

namespace ArbitraryPictureFormat.PaintDotNet;

public class ApfFileTypeFactory : IFileTypeFactory2
{
	public FileType[] GetFileTypeInstances(IFileTypeHost host)
	{
		return [new ApfFileType()];
	}
}
