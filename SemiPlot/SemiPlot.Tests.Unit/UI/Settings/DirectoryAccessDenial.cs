using System.Security.AccessControl;
using System.Security.Principal;

namespace SemiPlot.Tests.Unit.UI.Settings;

/// <summary>Refuses one kind of access to a directory until disposed.</summary>
internal sealed class DirectoryAccessDenial : IDisposable
{
	private const UnixFileMode FullAccess = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

	private readonly string _directory;
	private readonly FileSystemAccessRule? _denyRule;

	// Windows ignores the read-only attribute on a folder, so the refusal is an access rule there.
	private DirectoryAccessDenial(string directory, DeniedAccess denied)
	{
		_directory = directory;

		if (OperatingSystem.IsWindows())
		{
			var rights = denied == DeniedAccess.NewFolders
				? FileSystemRights.CreateDirectories
				: FileSystemRights.ListDirectory;
			var user = WindowsIdentity.GetCurrent().User!;
			_denyRule = new FileSystemAccessRule(user, rights, AccessControlType.Deny);
			var info = new DirectoryInfo(directory);
			var security = info.GetAccessControl();
			security.AddAccessRule(_denyRule);
			info.SetAccessControl(security);
		}
		else
		{
			File.SetUnixFileMode(directory, FullAccess & ~(denied == DeniedAccess.NewFolders
				? UnixFileMode.UserWrite
				: UnixFileMode.UserRead));
		}
	}

	private enum DeniedAccess
	{
		NewFolders,
		Listing
	}

	/// <summary>Refuses new folders inside the directory.</summary>
	public static DirectoryAccessDenial OfNewFolders(string directory)
	{
		return new DirectoryAccessDenial(directory, DeniedAccess.NewFolders);
	}

	/// <summary>Refuses listing the directory, while it still answers that it exists.</summary>
	public static DirectoryAccessDenial OfListing(string directory)
	{
		return new DirectoryAccessDenial(directory, DeniedAccess.Listing);
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
			File.SetUnixFileMode(_directory, FullAccess);
		}
	}
}
