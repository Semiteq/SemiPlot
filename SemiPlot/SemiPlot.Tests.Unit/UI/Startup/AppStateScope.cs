using System.Reflection;

using Avalonia;
using Avalonia.Styling;

using SemiPlot.UI;

namespace SemiPlot.Tests.Unit.UI.Startup;

/// <summary>
/// Puts back everything <c>App.Configure</c> writes into the headless application: the private fields it
/// sets, the locale resources and the theme variant. <c>Application.Current</c> outlives every test class,
/// and nothing on the production surface undoes a call.
/// </summary>
internal sealed class AppStateScope : IDisposable
{
	private static readonly FieldInfo[] _configuredFields =
	[
		AppField("_trendWindow"),
		AppField("_startupFailure"),
		AppField("_messagePanel"),
		AppField("_configDirectory")
	];

	private readonly object?[] _previousFields;
	private readonly Dictionary<object, object?> _previousResources;
	private readonly ThemeVariant? _previousVariant;

	public AppStateScope()
	{
		App = (App)Application.Current!;
		_previousFields = [.. _configuredFields.Select(field => field.GetValue(App))];
		_previousResources = App.Resources.ToDictionary(entry => entry.Key, entry => entry.Value);
		_previousVariant = App.RequestedThemeVariant;
	}

	public App App { get; }

	public void Dispose()
	{
		for (var index = 0; index < _configuredFields.Length; index++)
		{
			_configuredFields[index].SetValue(App, _previousFields[index]);
		}

		foreach (var key in App.Resources.Keys.ToArray())
		{
			if (!_previousResources.ContainsKey(key))
			{
				App.Resources.Remove(key);
			}
		}

		foreach (var entry in _previousResources)
		{
			App.Resources[entry.Key] = entry.Value;
		}

		App.RequestedThemeVariant = _previousVariant;
	}

	private static FieldInfo AppField(string name)
	{
		return typeof(App).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new InvalidOperationException($"App.{name} is gone, and the restore with it.");
	}
}
