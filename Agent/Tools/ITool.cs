using System.Text.Json;

namespace SimpleAgent.Tools;

public interface ITool
{
    string Name { get; }

    bool TryValidateInput(JsonElement input, out string error);

    Task<string> ExecuteAsync(JsonElement input);
}
