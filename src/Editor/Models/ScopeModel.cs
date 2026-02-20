using System.Text.Json.Serialization;

namespace MiniIT.SnipeInstaller.Editor.Models
{
    internal sealed class ScopeModel
    {
        public readonly string ScopeName;
        public readonly string RegistryUrl;
        public readonly string RegistryName;

        [JsonConstructor]
        public ScopeModel(string scopeName, string registryName, string registryUrl)
        {
            ScopeName = scopeName;
            RegistryUrl = registryUrl;
            RegistryName = registryName;
        }
    }
}
