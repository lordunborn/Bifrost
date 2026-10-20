using OmegaLauncher.Core.Models;
using System.Text.Json.Serialization;

namespace OmegaLauncher.Core.Serialization
{
    /// <summary>
    /// <see cref="JsonSerializerContext"/> implementation for AOT publish compatibility.
    /// </summary>
    [JsonSerializable(typeof(List<ServerInfo>))]
    [JsonSerializable(typeof(LaunchConfig))]
    [JsonSerializable(typeof(GuiConfig))]
    [JsonSerializable(typeof(OmegaLauncher.Core.Updates.DeploymentConfig))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    public partial class JsonContext : JsonSerializerContext
    {
    }
}
