using System;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using ArbitraryPictureFormat;

string[] pngs = Directory.GetFiles("data/png", "*.png");
foreach (var f in pngs.OrderBy(x => x)) {
    string name = Path.GetFileNameWithoutExtension(f);
    using Image img = Image.FromFile(f);
    var apf = new ArbitraryPicture(img);
    
    // Measure stencil
    using var stencilMs = new MemoryStream();
    apf.Descriptor.Serialize(stencilMs);
    long stencilSize = stencilMs.Length;
    
    // Full APF
    using var fullMs = new MemoryStream();
    apf.Save(fullMs);
    long totalSize = fullMs.Length;
    
    // Header = version(1) + stencil + background(4) + pixelcount(4) + mode(1) = 10 + stencil
    long headerSize = 1 + stencilSize + 4 + 4 + 1;
    long pixelDataSize = totalSize - headerSize;
    
    int pixelCount = apf.ImageData.Length;
    int totalPixels = apf.Descriptor.Width * apf.Descriptor.Height;
    
    // Count unique colors
    var unique = new HashSet<int>();
    for (int i = 0; i < apf.ImageData.Length; i++)
        unique.Add(apf.ImageData[i].ToArgb());
    
    Console.WriteLine($"{name,-20} Total:{totalSize,8}  Stencil:{stencilSize,7}  PixelData:{pixelDataSize,7}  " +
        $"Pixels:{pixelCount,7}/{totalPixels}  UniqueColors:{unique.Count,6}  PNG:{new FileInfo(f).Length,7}");
}
