using System.Globalization;
using System.Text.RegularExpressions;

using Opc.Ua;

namespace SemiPlot.DataSource.OpcUa;

/// <summary>
/// What a fresh browse of <c>Objects</c> means for the subscriptions built under the previous project ID.
/// </summary>
internal enum ProjectFolderChange
{
	Keep,
	Recreate,
	Missing
}

/// <summary>
/// The Simple-Scada project folder under <c>Objects</c>: its browse name is <c>root</c> in the SCADA's namespace
/// and its string identifier is <c>&lt;projectId&gt;root_&lt;n&gt;</c>; a pen's node is
/// <c>&lt;projectId&gt;&lt;penId&gt;</c>.
/// </summary>
internal static partial class ProjectFolder
{
	public const string NamespaceUri = "http://simple-scada.com/UA";

	public const string RootFolderName = "root";

	/// <summary>The project ID of the one matching child, or null when none or more than one matches.</summary>
	public static string? ProjectIdOf(IEnumerable<ReferenceDescription> objectsChildren, NamespaceTable namespaces)
	{
		var namespaceIndex = namespaces.GetIndex(NamespaceUri);
		string? found = null;

		foreach (var child in objectsChildren)
		{
			if (namespaceIndex < 0
				|| child.BrowseName.NamespaceIndex != namespaceIndex
				|| child.BrowseName.Name != RootFolderName
				|| !child.NodeId.TryGetValue(out string identifier))
			{
				continue;
			}

			var match = RootFolderIdentifier().Match(identifier);

			if (!match.Success)
			{
				continue;
			}

			if (found is not null)
			{
				return null;
			}

			found = match.Groups["projectId"].Value;
		}

		return found;
	}

	public static NodeId NodeOf(string projectId, ushort namespaceIndex, int penId)
	{
		return new NodeId(projectId + penId.ToString(CultureInfo.InvariantCulture), namespaceIndex);
	}

	public static ProjectFolderChange Next(string? current, string? browsed)
	{
		if (browsed is null)
		{
			return ProjectFolderChange.Missing;
		}

		return browsed == current ? ProjectFolderChange.Keep : ProjectFolderChange.Recreate;
	}

	[GeneratedRegex(@"^(?<projectId>.+)root_[0-9]+$", RegexOptions.CultureInvariant)]
	private static partial Regex RootFolderIdentifier();
}
