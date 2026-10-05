using FluentResults;

using Microsoft.Extensions.Logging;

using SemiPlot.UI.Messages;

namespace SemiPlot.UI.Minimap;

/// <summary>docs/architecture/trend-interaction.md#archive-overview-minimap</summary>
internal sealed class OutageReport(string readName, MessagePanelViewModel messagePanel, ILogger logger)
{
	private bool _isInOutage;

	public void Succeeded()
	{
		_isInOutage = false;
	}

	public void Failed(IReadOnlyList<IError> errors)
	{
		if (_isInOutage)
		{
			var error = errors[0];
			logger.LogWarning(
				(error as IExceptionalError)?.Exception, "The {Read} failed again: {Reason}", readName, error.Message);

			return;
		}

		_isInOutage = true;
		messagePanel.TryReportFailure(errors, logger);
	}
}
