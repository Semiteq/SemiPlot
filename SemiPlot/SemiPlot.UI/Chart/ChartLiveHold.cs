namespace SemiPlot.UI.Chart;

/// <summary>The one writer of the edge X every pen is held to: the newest sample while the session is up.</summary>
internal sealed class ChartLiveHold : IDisposable
{
	private readonly ChartPenSet _penSet;
	private readonly ChartNavigationController _navigation;
	private bool _isSessionUp;

	public ChartLiveHold(ChartPenSet penSet, ChartNavigationController navigation)
	{
		_penSet = penSet;
		_navigation = navigation;
		_navigation.NewestSampleMoved += OnNewestSampleMoved;
	}

	public void SetSessionUp(bool isUp)
	{
		if (_isSessionUp == isUp)
		{
			return;
		}

		_isSessionUp = isUp;
		Apply();
	}

	public void Apply()
	{
		var edgeX = _isSessionUp && _navigation.NewestSample is { } newest
			? LocalTimeAxis.ToAxis(newest)
			: double.NaN;

		_penSet.HoldLinesTo(edgeX);
	}

	public void Dispose()
	{
		_navigation.NewestSampleMoved -= OnNewestSampleMoved;
	}

	private void OnNewestSampleMoved(object? sender, DateTime newestSample)
	{
		Apply();
	}
}
