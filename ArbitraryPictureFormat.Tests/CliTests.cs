using System.Diagnostics;
using System.Drawing;
using ArbitraryPictureFormat;

namespace ArbitraryPictureFormat.Tests;

public class CliTests
{
	[Fact]
	public void Decode_MissingLayer_ReturnsNonZeroAndDoesNotWriteOutput()
	{
		string tempDir = Path.Combine(Path.GetTempPath(), "apf-cli-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempDir);

		try
		{
			string input = Path.Combine(tempDir, "layers.apf");
			string output = Path.Combine(tempDir, "missing.png");

			var pixels = new[] { Color.Red, Color.Green, Color.Blue, Color.White };
			var file = new ApfFile(new[]
			{
				new ApfImage(new ArbitraryPicture(2, 2, pixels), "diffuse"),
				new ApfImage(new ArbitraryPicture(2, 2, pixels), "normal")
			});
			file.Save(input);

			using Process process = StartApf(input, "-l", "missing", "-o", output);

			Assert.NotEqual(0, process.ExitCode);
			Assert.Contains("no image found with name 'missing'", process.StandardError.ReadToEnd());
			Assert.False(File.Exists(output));
		}
		finally
		{
			Directory.Delete(tempDir, recursive: true);
		}
	}

	static Process StartApf(params string[] arguments)
	{
		string exePath = Path.Combine(AppContext.BaseDirectory, "apf.exe");
		Assert.True(File.Exists(exePath), $"Expected CLI executable at {exePath}");

		var startInfo = new ProcessStartInfo(exePath)
		{
			RedirectStandardError = true,
			RedirectStandardOutput = true,
			UseShellExecute = false
		};

		foreach (string argument in arguments)
			startInfo.ArgumentList.Add(argument);

		Process process = Process.Start(startInfo)!;
		Assert.True(process.WaitForExit(30000), "CLI process timed out.");
		return process;
	}
}
