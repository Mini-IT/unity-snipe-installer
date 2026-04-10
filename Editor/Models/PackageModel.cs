using System.Text.Json.Serialization;

namespace MiniIT.SnipeInstaller.Editor.Models
{
    internal sealed class PackageModel
    {
        public readonly string Name;
        public readonly string[] Dependencies;

        public readonly ScopeModel Scope;

        [JsonConstructor]
        public PackageModel(string name, string[] dependencies, ScopeModel scope)
        {
            Name = name;
            Scope = scope;
            Dependencies = dependencies;
        }
    }
}
