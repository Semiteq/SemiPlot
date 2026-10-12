namespace SemiPlot.Tests.Unit;

/// <summary>A fresh directory under the system temp folder that a dispose removes, read-only files included.</summary>
internal sealed class TemporaryDirectory(string prefix) : IDisposable
{
	public string Path { get; } = Directory.CreateTempSubdirectory(prefix + "-").FullName;

	public static implicit operator string(TemporaryDirectory directory)
	{
		return directory.Path;
	}

	public void Dispose()
	{
		if (!Directory.Exists(Path))
		{
			return;
		}

		foreach (var file in Directory.GetFiles(Path, "*", SearchOption.AllDirectories))
		{
			File.SetAttributes(file, FileAttributes.Normal);
		}

		Directory.Delete(Path, recursive: true);
	}

	public override string ToString()
	{
		return Path;
	}
}
