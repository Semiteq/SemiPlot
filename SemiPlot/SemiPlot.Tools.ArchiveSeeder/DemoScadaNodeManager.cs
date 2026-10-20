using Opc.Ua;
using Opc.Ua.Server;

using SemiPlot.DataSource.Postgres;

namespace SemiPlot.Tools.ArchiveSeeder;

/// <summary>The demo server's address space, shaped like Simple-Scada's own namespace.</summary>
internal sealed class DemoScadaNodeManager : CustomNodeManager2
{
	private const string NamespaceUri = "http://simple-scada.com/UA";
	private const string DiagnosticsNamespaceUri = "http://opcfoundation.org/UA/Diagnostics";
	private const string ProjectId = "5E4D0C1A0B";
	private const string RootFolderName = "root";

	// The real server gives _1 to its system folder, which the demo does not serve.
	private const int RootFolderNumber = 2;

	private readonly FollowOptions _options;
	private readonly ArchiveTimeConverter _converter;
	private readonly DateTime _startUtc;
	private readonly DateTime _startLocal;
	private readonly Dictionary<int, BaseDataVariableState> _variables = [];

	private DemoScadaNodeManager(
		IServerInternal server,
		ApplicationConfiguration configuration,
		FollowOptions options,
		ArchiveTimeConverter converter,
		DateTime startUtc,
		DateTime startLocal)
		: base(server, configuration, NamespaceUri)
	{
		_options = options;
		_converter = converter;
		_startUtc = startUtc;
		_startLocal = startLocal;
	}

	public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
	{
		lock (Lock)
		{
			if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var objects))
			{
				objects = [];
				externalReferences[ObjectIds.ObjectsFolder] = objects;
			}

			var pens = RawLayerGenerator.SelectPens(_options.PenCount);
			var startValues = LiveTailGenerator.ChangesAt(_options, _startLocal)
				.ToDictionary(row => row.Id, row => row.Value);
			var root = CreateFolder(objects, RootFolderName, RootFolderNumber);

			foreach (var pen in pens)
			{
				var variable = CreateVariable(root, pen, startValues[pen.PenId]);
				root.AddChild(variable);
				_variables.Add(pen.PenId, variable);
			}

			var groups = pens
				.Where(pen => pen.PrimaryGroup.Length > 0)
				.GroupBy(pen => pen.PrimaryGroup, StringComparer.Ordinal);
			var folderNumber = RootFolderNumber;

			foreach (var group in groups)
			{
				var folder = CreateFolder(objects, group.Key, ++folderNumber);

				foreach (var pen in group)
				{
					folder.AddReference(ReferenceTypeIds.Organizes, false, _variables[pen.PenId].NodeId);
				}

				AddPredefinedNode(SystemContext, folder);
			}

			AddPredefinedNode(SystemContext, root);
		}
	}

	/// <summary>Writes each row into its pen's variable, stamped with the row's instant in UTC.</summary>
	public void Write(IReadOnlyList<ArchiveRow> rows)
	{
		lock (Lock)
		{
			foreach (var row in rows)
			{
				var variable = _variables[row.Id];
				variable.Value = new Variant(row.Value);
				variable.Timestamp = _converter.ToUtc(row.Timestamp);
				variable.ClearChangeMasks(SystemContext, false);
			}
		}
	}

	private FolderState CreateFolder(IList<IReference> objects, string name, int number)
	{
		var folder = new FolderState(null)
		{
			SymbolicName = name,
			NodeId = new NodeId($"{ProjectId}{name}_{number}", NamespaceIndex),
			BrowseName = new QualifiedName(name, NamespaceIndex),
			DisplayName = new LocalizedText(name),
			TypeDefinitionId = ObjectTypeIds.FolderType,
			ReferenceTypeId = ReferenceTypeIds.Organizes,
		};
		folder.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
		objects.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, folder.NodeId));

		return folder;
	}

	private BaseDataVariableState CreateVariable(FolderState root, SyntheticPen pen, double value)
	{
		return new BaseDataVariableState(root)
		{
			SymbolicName = pen.Name,
			NodeId = new NodeId($"{ProjectId}{pen.PenId}", NamespaceIndex),
			BrowseName = new QualifiedName(pen.Name, NamespaceIndex),
			DisplayName = new LocalizedText(pen.Name),
			TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
			ReferenceTypeId = ReferenceTypeIds.Organizes,
			DataType = DataTypeIds.Double,
			ValueRank = ValueRanks.Scalar,
			AccessLevel = AccessLevels.CurrentRead,
			UserAccessLevel = AccessLevels.CurrentRead,
			Value = new Variant(value),
			StatusCode = StatusCodes.Good,
			Timestamp = _startUtc,
		};
	}

	/// <summary>Builds the manager during the server's start and keeps it for the publishing loop.</summary>
	internal sealed class Factory(
		FollowOptions options,
		ArchiveTimeConverter converter,
		DateTime startUtc,
		DateTime startLocal)
		: INodeManagerFactory
	{
		public DemoScadaNodeManager? Manager { get; private set; }

		public ArrayOf<string> NamespacesUris => [NamespaceUri];

		// Simple-Scada serves its variables at namespace index 3: registering Diagnostics first moves it from
		// 3 to 2 and leaves 3 to the manager.
		public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
		{
			server.NamespaceUris.GetIndexOrAppend(DiagnosticsNamespaceUri);
			Manager = new DemoScadaNodeManager(server, configuration, options, converter, startUtc, startLocal);

			return Manager;
		}
	}
}
