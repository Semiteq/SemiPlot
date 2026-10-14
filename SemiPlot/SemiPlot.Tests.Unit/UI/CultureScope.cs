using System.Globalization;

namespace SemiPlot.Tests.Unit.UI;

/// <summary>Sets the cultures of the calling thread, the UI one apart when named, and puts them back when disposed.</summary>
internal sealed class CultureScope : IDisposable
{
	private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
	private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;

	public CultureScope(string cultureName, string? uiCultureName = null)
	{
		CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
		CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(uiCultureName ?? cultureName);
	}

	public void Dispose()
	{
		CultureInfo.CurrentCulture = _previousCulture;
		CultureInfo.CurrentUICulture = _previousUiCulture;
	}
}
