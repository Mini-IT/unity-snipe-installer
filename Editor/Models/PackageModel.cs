using System;

namespace MiniIT.SnipeInstaller.Editor.Models
{
    [Serializable]
    internal sealed class PackageModel
    {
        public string Name;
        public string[] Dependencies;
        public ScopeModel Scope;
    }
}
