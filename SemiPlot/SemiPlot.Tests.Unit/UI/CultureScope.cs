using System.Globalization;

namespace SemiPlot.Tests.Unit.UI;

/// <summary>Sets both cultures of the calling thread and puts them back when disposed.</summary>
internal sealed class CultureScope : IDisposable
{
	private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
	private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;

	public CultureScope(string cultureName)
	{
		var culture = CultureInfo.GetCultureInfo(cultureName);

		CultureInfo.CurrentCulture = culture;
		CultureInfo.CurrentUICulture = culture;
	}

	public void Dispose()
	{
		CultureInfo.CurrentCulture = _previousCulture;
		CultureInfo.CurrentUICulture = _previousUiCulture;
	}
}
