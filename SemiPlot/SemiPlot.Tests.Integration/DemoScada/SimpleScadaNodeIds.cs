using Opc.Ua;

namespace SemiPlot.Tests.Integration.DemoScada;

/// <summary>The node ids the demo server serves, spelled the way Simple-Scada spells its own.</summary>
public static class SimpleScadaNodeIds
{
	public const string NamespaceUri = "http://simple-scada.com/UA";
	public const ushort NamespaceIndex = 3;
	public const string RootFolderName = "root";
	public const int RootFolderNumber = 2;
	public const int FirstGroupFolderNumber = 3;

	private const string ProjectId = "5E4D0C1A0B";

	public static NodeId PenNode(int penId)
	{
		return new NodeId($"{ProjectId}{penId}", NamespaceIndex);
	}

	public static NodeId FolderNode(string name, int number)
	{
		return new NodeId($"{ProjectId}{name}_{number}", NamespaceIndex);
	}
}
