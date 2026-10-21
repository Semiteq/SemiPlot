using AwesomeAssertions;

using Opc.Ua;

using SemiPlot.DataSource.OpcUa;

using Xunit;

namespace SemiPlot.Tests.Unit.OpcUa;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ProjectFolderTests
{
	private const ushort ScadaIndex = 3;

	private static readonly NamespaceTable _realNamespaces = new(
	[
		Namespaces.OpcUa,
		"urn:WORKING-PC:SimpleScada:UAServer",
		"http://www.simple-scada.com/UA/Namespace",
		ProjectFolder.NamespaceUri,
	]);

	private static readonly NamespaceTable _demoNamespaces = new(
	[
		Namespaces.OpcUa,
		"urn:localhost:SemiPlotDemoScada",
		"http://opcfoundation.org/UA/Diagnostics",
		ProjectFolder.NamespaceUri,
	]);

	[Fact]
	public void TheRealObjectsOrder_YieldsTheProjectId()
	{
		ReferenceDescription[] children =
		[
			Standard("Server", ObjectIds.Server),
			Folder("system", "FD34514A35system_1"),
			Folder("root", "FD34514A35root_2"),
			Folder("group1", "FD34514A35group1_3"),
		];

		ProjectFolder.ProjectIdOf(children, _realNamespaces).Should().Be("FD34514A35");
	}

	[Fact]
	public void TheDemoObjectsOrder_YieldsTheProjectId()
	{
		ReferenceDescription[] children =
		[
			Standard("Server", ObjectIds.Server),
			Standard("Aliases", new NodeId(23470u)),
			Standard("Locations", new NodeId(31915u)),
			Folder("root", "5E4D0C1A0Broot_2"),
			Folder("Chamber", "5E4D0C1A0BChamber_3"),
			Folder("Gas", "5E4D0C1A0BGas_4"),
		];

		ProjectFolder.ProjectIdOf(children, _demoNamespaces).Should().Be("5E4D0C1A0B");
	}

	[Fact]
	public void ARootInAnotherNamespace_YieldsNothing()
	{
		ReferenceDescription[] children =
		[
			Standard("Server", ObjectIds.Server),
			Folder("root", "FD34514A35root_2", namespaceIndex: 2),
		];

		ProjectFolder.ProjectIdOf(children, _realNamespaces).Should().BeNull();
	}

	[Theory]
	[InlineData("FD34514A35root")]
	[InlineData("FD34514A35root_")]
	[InlineData("FD34514A35root_2x")]
	[InlineData("root_2")]
	public void ARootWhoseIdentifierDoesNotEndRootNumber_YieldsNothing(string identifier)
	{
		ReferenceDescription[] children = [Folder("root", identifier)];

		ProjectFolder.ProjectIdOf(children, _realNamespaces).Should().BeNull();
	}

	[Fact]
	public void TwoRootFolders_YieldNothing()
	{
		ReferenceDescription[] children =
		[
			Folder("root", "FD34514A35root_2"),
			Folder("root", "AB12345678root_5"),
		];

		ProjectFolder.ProjectIdOf(children, _realNamespaces).Should().BeNull();
	}

	[Fact]
	public void NoRootFolder_YieldsNothing()
	{
		ReferenceDescription[] children =
		[
			Standard("Server", ObjectIds.Server),
			Folder("group1", "FD34514A35group1_3"),
		];

		ProjectFolder.ProjectIdOf(children, _realNamespaces).Should().BeNull();
	}

	[Fact]
	public void AServerWithoutTheScadasNamespace_YieldsNothing()
	{
		var namespaces = new NamespaceTable([Namespaces.OpcUa, "urn:localhost:Other"]);
		ReferenceDescription[] children = [Folder("root", "FD34514A35root_2", namespaceIndex: 1)];

		ProjectFolder.ProjectIdOf(children, namespaces).Should().BeNull();
	}

	[Fact]
	public void ThePensNode_IsTheProjectIdFollowedByThePenId()
	{
		ProjectFolder.NodeOf("FD34514A35", ScadaIndex, 1005).ToString().Should().Be("ns=3;s=FD34514A351005");
	}

	[Fact]
	public void Next_KeepsAnEqualId()
	{
		ProjectFolder.Next("FD34514A35", "FD34514A35").Should().Be(ProjectFolderChange.Keep);
	}

	[Theory]
	[InlineData("FD34514A35", "AB12345678")]
	[InlineData(null, "FD34514A35")]
	public void Next_RecreatesOnADifferentId(string? current, string browsed)
	{
		ProjectFolder.Next(current, browsed).Should().Be(ProjectFolderChange.Recreate);
	}

	[Theory]
	[InlineData("FD34514A35")]
	[InlineData(null)]
	public void Next_ReportsMissingOnNone(string? current)
	{
		ProjectFolder.Next(current, null).Should().Be(ProjectFolderChange.Missing);
	}

	private static ReferenceDescription Standard(string name, NodeId node)
	{
		return new ReferenceDescription
		{
			BrowseName = new QualifiedName(name, 0),
			NodeId = new ExpandedNodeId(node),
		};
	}

	private static ReferenceDescription Folder(string name, string identifier, ushort namespaceIndex = ScadaIndex)
	{
		return new ReferenceDescription
		{
			BrowseName = new QualifiedName(name, namespaceIndex),
			NodeId = new ExpandedNodeId(new NodeId(identifier, namespaceIndex)),
		};
	}
}
