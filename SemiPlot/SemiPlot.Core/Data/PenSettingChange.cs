using SemiPlot.Core.Trends;

namespace SemiPlot.Core.Data;

/// <summary>
/// One pen setting to write, one column per arm; the scale pair is one arm. A null unit or mask is written
/// as <c>NULL</c>.
/// </summary>
public abstract record PenSettingChange
{
	private PenSettingChange()
	{
	}

	public sealed record Name(string Value) : PenSettingChange;

	public sealed record Unit(string? Value) : PenSettingChange;

	public sealed record Format(string? Value) : PenSettingChange;

	public sealed record Color(string Value) : PenSettingChange;

	public sealed record LineStyle(PenLineStyle Value) : PenSettingChange;

	public sealed record EnabledOnStart(bool Value) : PenSettingChange;

	public sealed record ScaleOnStart(double? Min, double? Max) : PenSettingChange;
}
