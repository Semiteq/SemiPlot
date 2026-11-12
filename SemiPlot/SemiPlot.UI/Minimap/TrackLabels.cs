using Avalonia;
using Avalonia.Controls;

namespace SemiPlot.UI.Minimap;

/// <summary>The minimap's label rows: measuring, placing and hiding their labels.</summary>
internal static class TrackLabels
{
	public static double MeasuredWidth(Control control)
	{
		control.Measure(Size.Infinity);

		return control.DesiredSize.Width;
	}

	/// <summary>The widest of the texts, measured on the row so that the row's text style applies.</summary>
	public static double Widest(Canvas row, IEnumerable<string> texts)
	{
		var probe = new TextBlock();
		row.Children.Add(probe);
		var widest = 0.0;
		foreach (var text in texts)
		{
			probe.Text = text;
			widest = Math.Max(widest, MeasuredWidth(probe));
		}

		row.Children.Remove(probe);

		return widest;
	}

	/// <summary>One label per tick at its x; a label that would overflow the row's right end is dropped.</summary>
	public static void Layout(Canvas row, IEnumerable<(double X, string Text)> labels, double width)
	{
		row.Children.Clear();
		foreach (var (x, text) in labels)
		{
			var label = new TextBlock { Text = text };
			row.Children.Add(label);
			if (x + MeasuredWidth(label) > width)
			{
				row.Children.Remove(label);

				continue;
			}

			Canvas.SetLeft(label, x);
		}
	}

	/// <summary>Hides every label of the row that the span overlaps and shows the rest.</summary>
	public static void Cover(Canvas labels, double left, double width)
	{
		foreach (var label in labels.Children)
		{
			var labelLeft = Canvas.GetLeft(label);
			var isCovered = width > 0.0 && labelLeft < left + width && left < labelLeft + label.DesiredSize.Width;
			label.Opacity = isCovered ? 0.0 : 1.0;
		}
	}
}
