using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.UI.Messages;
using SemiPlot.UI.Settings;

namespace SemiPlot.Tests.Unit.UI.Settings;

/// <summary>Builds the settings view model over a copy of the shipped set.</summary>
internal static class SettingsViewModelStand
{
	public static SettingsViewModel Build(
		string configDirectory,
		MessagePanelViewModel messagePanel,
		Action? restartApplication = null)
	{
		return new SettingsViewModel(
			configDirectory,
			SettingsSave.ReadOwned(configDirectory),
			messagePanel,
			NullLogger<SettingsViewModel>.Instance,
			restartApplication ?? (() => { }));
	}
}
