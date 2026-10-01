using FluentResults;

namespace SemiPlot.UI.Startup;

public sealed class InstanceHostUnknownError()
	: Error("The executable of the running process is unknown, so no copy can be started.");
