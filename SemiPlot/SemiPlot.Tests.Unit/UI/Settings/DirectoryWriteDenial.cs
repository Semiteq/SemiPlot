using System.Security.AccessControl;
using System.Security.Principal;

namespace SemiPlot.Tests.Unit.UI.Settings;

/// <summary>Refuses new folders inside a directory until disposed.</summary>
internal sealed class DirectoryWriteDenial : IDisposable
{
	private readonly string _directory;
	private readonly FileSystemAccessRule? _denyRule;

	// Windows ignores the read-only attribute on a folder, so the refusal is an access rule there.
	public DirectoryWriteDenial(string directory)
	{
		_directory = directory;

		if (OperatingSystem.IsWindows())
		{
			var user = WindowsIdentity.GetCurrent().User!;
			_denyRule = new FileSystemAccessRule(user, FileSystemRights.CreateDirectories, AccessControlType.Deny);
			var info = new DirectoryInfo(directory);
			var security = info.GetAccessControl();
			security.AddAccessRule(_denyRule);
			info.SetAccessControl(security);
		}
		else
		{
			File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
		}
	}

	public void Dispose()
	{
		if (OperatingSystem.IsWindows())
		{
			if (_denyRule is not null)
			{
				var info = new DirectoryInfo(_directory);
				var security = info.GetAccessControl();
				security.RemoveAccessRule(_denyRule);
				info.SetAccessControl(security);
			}
		}
		else
		{
			File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}
	}
}
