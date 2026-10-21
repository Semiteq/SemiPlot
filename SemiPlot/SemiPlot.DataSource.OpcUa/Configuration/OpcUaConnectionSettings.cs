namespace SemiPlot.DataSource.OpcUa.Configuration;

/// <summary>The SCADA's OPC UA server and the folder the client's certificate stores sit under.</summary>
public sealed record OpcUaConnectionSettings(string Host, int Port, string CertificateStoreRoot)
{
	public string EndpointUrl => $"opc.tcp://{Host}:{Port}";
}
