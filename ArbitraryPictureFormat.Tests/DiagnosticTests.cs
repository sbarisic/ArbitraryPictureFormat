using System.Drawing;
using ArbitraryPictureFormat;
using System.Collections.Generic;

namespace ArbitraryPictureFormat.Tests;

public class DiagnosticTests {
    static string DataDir => Path.Combine(AppContext.BaseDirectory, "data", "png");

    [Fact]
    public void PrintSizeBreakdown() {
        foreach (string f in Directory.GetFiles(DataDir, "*.png").OrderBy(x => x)) {
            string name = Path.GetFileNameWithoutExtension(f);
            using Image img = Image.FromFile(f);
            var apf = new ArbitraryPicture(img);

            using var stencilMs = new MemoryStream();
            apf.Descriptor.Serialize(stencilMs);
            long stencilSize = stencilMs.Length;

            using var fullMs = new MemoryStream();
            apf.Serialize(fullMs);
            long totalSize = fullMs.Length;

            long headerSize = 1 + stencilSize + 4 + 4 + 1;
            long pixelDataSize = totalSize - headerSize;
            int pixelCount = apf.ImageData.Length;
            int totalPixels = apf.Descriptor.Width * apf.Descriptor.Height;

            var unique = new HashSet<int>();
            for (int i = 0; i < apf.ImageData.Length; i++)
                unique.Add(apf.ImageData[i].ToArgb());

            long pngSize = new FileInfo(f).Length;
            Console.WriteLine($"{name,-20} Total:{totalSize,8}  Stencil:{stencilSize,7}  PixelData:{pixelDataSize,7}  Pixels:{pixelCount,7}/{totalPixels}  Unique:{unique.Count,6}  PNG:{pngSize,7}  Ratio:{(double)totalSize/pngSize:F2}x");
        }
    }
}
