using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;

using ReactiveUI;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Minimap;

public partial class MinimapView : UserControl
{
	private readonly CompositeDisposable _disposables = [];

	public MinimapView()
	{
		InitializeComponent();

		AttachDetailTrackInput();

		StripCanvas.GetObservable(BoundsProperty).Subscribe(_ =>
		{
			ReportStripWidth();
			UpdateStrip();
		});

		DataContextChanged += OnDataContextChanged;
	}

	private void OnDataContextChanged(object? sender, EventArgs e)
	{
		_disposables.Clear();

		if (DataContext is not MinimapViewModel viewModel)
		{
			return;
		}

		ReportStripWidth();
		_disposables.Add(viewModel
			.WhenAnyValue(
				model => model.HasExtent,
				model => model.Pager.Page,
				model => model.BandFeed.Band)
			.Subscribe(_ => LayoutBand()));
		_disposables.Add(FollowDetailTrack(viewModel));
	}

	private void ReportStripWidth()
	{
		if (DataContext is MinimapViewModel viewModel)
		{
			viewModel.BandFeed.SetStripWidth(StripCanvas.Bounds.Width);
		}
	}

	private void UpdateStrip()
	{
		LayoutBand();
		LayoutDetailTrack();
	}

	private void LayoutBand()
	{
		if (DataContext is not MinimapViewModel viewModel)
		{
			return;
		}

		var (width, height) = (StripCanvas.Bounds.Width, StripCanvas.Bounds.Height);
		BandLayer.Width = width;
		BandLayer.Height = height;
		var page = viewModel.Pager.Page;
		BandLayer.Figures = viewModel is { HasExtent: true, BandFeed.Band: { } band }
			? MinimapGeometry.BandFigures(band, page.FromUtc, page.ToUtc, width, height)
			: [];
	}
}
