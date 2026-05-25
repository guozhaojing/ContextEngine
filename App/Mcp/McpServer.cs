using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace App.Mcp;

public sealed class McpServer
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ContextEngineMcpTools _tools;
    private string _serverName;
    private string _serverVersion = "1.0.0";

    public McpServer(ContextEngineMcpTools tools, string serverName = "ContextEngine")
    {
        _tools = tools;
        _serverName = serverName;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        var reader = new StreamReader(stdin);
        var writer = new StreamWriter(stdout) { AutoFlush = true };

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            if (!line.StartsWith("Content-Length:", StringComparison.Ordinal))
                continue;

            var length = int.Parse(line["Content-Length:".Length..].Trim());
            await reader.ReadLineAsync(ct); // skip empty line

            var buffer = new char[length];
            var read = 0;
            while (read < length)
                read += await reader.ReadAsync(buffer, read, length - read);

            var json = new string(buffer, 0, length);
            var response = HandleMessage(json);
            if (response is null) continue;

            var responseJson = JsonSerializer.Serialize(response, JsonOpts);
            await writer.WriteAsync($"Content-Length: {Encoding.UTF8.GetByteCount(responseJson)}\r\n\r\n{responseJson}\r\n");
        }
    }

    private McpResponse? HandleMessage(string json)
    {
        var msg = JsonSerializer.Deserialize<McpMessage>(json, JsonOpts);
        if (msg is null) return null;

        return msg.Method switch
        {
            "initialize" => HandleInitialize(json),
            "tools/list" => HandleToolsList(msg.Id),
            "tools/call" => HandleToolsCall(msg.Id, json),
            _ => null,
        };
    }

    private McpResponse HandleInitialize(string json)
    {
        var init = JsonSerializer.Deserialize<InitializeRequest>(json, JsonOpts);
        if (init?.Params?.ProtocolVersion is not null)
            _serverVersion = init.Params.ProtocolVersion;

        return new McpResponse
        {
            Id = init?.Id,
            Result = new InitializeResult
            {
                ProtocolVersion = _serverVersion,
                Capabilities = new ServerCapabilities
                {
                    Tools = new(),
                },
                ServerInfo = new ServerInfo
                {
                    Name = _serverName,
                    Version = "1.0.0",
                },
            },
        };
    }

    private McpResponse HandleToolsList(object? id)
    {
        var list = _tools.Definitions.Select(t => new
        {
            name = t.Name,
            description = t.Description,
            inputSchema = new
            {
                type = "object",
                properties = t.Parameters.ToDictionary(
                    p => p.Name,
                    p => new { type = p.Type, description = p.Description }),
                required = t.Parameters.Where(p => p.Required).Select(p => p.Name).ToArray(),
            },
        }).ToList();

        return new McpResponse { Id = id, Result = new { tools = list } };
    }

    private McpResponse HandleToolsCall(object? id, string json)
    {
        var call = JsonSerializer.Deserialize<ToolCallRequest>(json, JsonOpts);
        if (call?.Params is null)
            return new McpResponse { Id = id, Error = new McpError { Code = -1, Message = "Invalid request" } };

        try
        {
            var result = _tools.Invoke(call.Params.Name, call.Params.Arguments);
            var content = new { type = "text", text = JsonSerializer.Serialize(result, JsonOpts) };
            return new McpResponse { Id = id, Result = new { content = new[] { content } } };
        }
        catch (KeyNotFoundException ex)
        {
            return new McpResponse { Id = id, Error = new McpError { Code = -2, Message = ex.Message } };
        }
        catch (Exception ex)
        {
            return new McpResponse { Id = id, Error = new McpError { Code = -3, Message = ex.Message } };
        }
    }
}

// ── JSON-RPC / MCP types ──

public sealed class McpMessage
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";
}

public sealed class InitializeRequest
{
    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("params")]
    public InitParams? Params { get; set; }
}

public sealed class InitParams
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "0.1.0";
}

public sealed class ToolCallRequest
{
    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("params")]
    public ToolCallParams? Params { get; set; }
}

public sealed class ToolCallParams
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("arguments")]
    public Dictionary<string, JsonElement> Arguments { get; set; } = new();
}

public sealed class McpResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public McpError? Error { get; set; }
}

public sealed class McpError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public sealed class InitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "0.1.0";

    [JsonPropertyName("capabilities")]
    public ServerCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("serverInfo")]
    public ServerInfo ServerInfo { get; set; } = new();
}

public sealed class ServerCapabilities
{
    [JsonPropertyName("tools")]
    public ToolCapability Tools { get; set; } = new();
}

public sealed class ToolCapability { }

public sealed class ServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
}
