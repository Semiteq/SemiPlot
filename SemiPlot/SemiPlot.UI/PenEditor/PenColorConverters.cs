using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SemiPlot.UI.PenEditor;

public static class PenColorConverters
{
	private const int StoredColorLength = 7;

	/// <summary>The picker's colour: the draft's, and transparent for an empty or malformed draft.</summary>
	public static readonly FuncValueConverter<string?, Color> ToColor =
		new(color => TryParse(color, out var parsed) ? parsed : Colors.Transparent);

	/// <summary>Reads only the #RRGGBB form the archive stores; Color.TryParse alone also takes names.</summary>
	internal static bool TryParse(string? color, out Color parsed)
	{
		parsed = default;

		return color is { Length: StoredColorLength }
			&& color[0] == '#'
			&& color.Skip(1).All(char.IsAsciiHexDigit)
			&& Color.TryParse(color, out parsed);
	}

	/// <summary>The #RRGGBB form the archive stores and <see cref="TryParse"/> reads; the alpha is dropped.</summary>
	internal static string Format(Color color)
	{
		return FormattableString.Invariant($"#{color.R:X2}{color.G:X2}{color.B:X2}");
	}
}
