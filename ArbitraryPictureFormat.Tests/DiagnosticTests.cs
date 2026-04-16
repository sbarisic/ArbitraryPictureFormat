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

    [Fact]
    public void RansRoundTrip() {
        void Test(string name, byte[] data) {
            byte[] enc = Helpers.RansEncode(data);
            byte[] dec = Helpers.RansDecode(enc, data.Length);
            Console.WriteLine($"rANS {name}: enc={enc.Length} data={data.Length} match={data.SequenceEqual(dec)}");
            Assert.Equal(data, dec);
        }

        void TestCompress(string name, byte[] data) {
            byte[] comp = Helpers.Compress(data);
            byte[] dec = Helpers.Decompress(comp, data.Length);
            Console.WriteLine($"Compress {name}: mode={comp[0]} comp={comp.Length} data={data.Length} match={data.SequenceEqual(dec)}");
            Assert.Equal(data, dec);
        }

        Test("6bytes", new byte[] { 0, 1, 2, 3, 4, 5 });
        Test("allFF", Enumerable.Repeat((byte)0xFF, 100).ToArray());
        Test("twoSym", new byte[] { 0, 0, 0, 0xFF, 0xFF });
        Test("allZero", new byte[500]);
        Test("single", new byte[] { 42 });
        Test("sequential", Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());

        TestCompress("random", Enumerable.Range(0, 1000).Select(i => (byte)(i * 37 % 256)).ToArray());
        TestCompress("stencilLike", Enumerable.Repeat((byte)0xFF, 3750).Concat(Enumerable.Repeat((byte)0x00, 250)).ToArray());
    }

    [Fact]
    public void RansStencilRoundTrip() {
        // Test rANS with actual stencil data from each image
        foreach (string f in Directory.GetFiles(DataDir, "*.png").OrderBy(x => x)) {
            string name = Path.GetFileNameWithoutExtension(f);
            using Image img = Image.FromFile(f);
            var apf = new ArbitraryPicture(img);

            // Serialize stencil to bytes
            using var ms = new MemoryStream();
            apf.Descriptor.Serialize(ms);
            byte[] stencilData = ms.ToArray();

            // Now try full serialize/deserialize
            using var fullMs = new MemoryStream();
            try {
                apf.Serialize(fullMs);
                fullMs.Position = 0;
                var loaded = new ArbitraryPicture(fullMs);
                Console.WriteLine($"Stencil {name}: OK (stencil={stencilData.Length}, pixels={apf.ImageData.Length})");
            } catch (Exception ex) {
                Console.WriteLine($"Stencil {name}: FAIL {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }
    }
}
