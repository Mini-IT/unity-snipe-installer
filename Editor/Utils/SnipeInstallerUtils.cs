using MiniIT.SnipeInstaller.Editor.Models;
using System.Security.Cryptography;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Text;
using System.IO;
using System;

namespace MiniIT.SnipeInstaller.Editor.Utils
{
    internal static class SnipeInstallerUtils
    {
        private const string SCOPED_REGISTIRES = "scopedRegistries";
        private const string DEPENDENCIES = "dependencies";
        private const string VERSION = "version";
        private const string ORG_NUGET_PREFIX = "org.nuget.";

        internal static string GetPackagesHash()
        {
            var asset = Resources.Load<TextAsset>("packages");

            if (asset == null)
            {
                return string.Empty;
            }

            byte[] data = Encoding.UTF8.GetBytes(asset.text);

            using var sha = SHA256.Create();

            byte[] hash = sha.ComputeHash(data);
            var sb = new StringBuilder(hash.Length * 2);

            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("x2"));
            }

            return sb.ToString();
        }

        internal static PackageModel[] GetRequiredPackages()
        {
            var packagesTextAsset = Resources.Load<TextAsset>("packages");

            if (packagesTextAsset == null)
            {
                return Array.Empty<PackageModel>();
            }

            var rootModel = new RequiredPackagesRootModel();

            try
            {
                EditorJsonUtility.FromJsonOverwrite(packagesTextAsset.text, rootModel);
            }
            catch
            {
                return Array.Empty<PackageModel>();
            }

            return rootModel.list ?? Array.Empty<PackageModel>();
        }

        internal static bool TryAddScopeRegistry(string scopeName, string registryName, string registryUrl)
        {
            if (!IsScopeValid(scopeName, registryUrl))
            {
                return false;
            }

            string manifestPath = GetManifestPath();

            if (!File.Exists(manifestPath))
            {
                return false;
            }

            string manifestJson = File.ReadAllText(manifestPath);

            if (!TryParseJsonObjectEntries(manifestJson, out var manifestEntries))
            {
                return false;
            }

            var scopedRegistries = GetScopedRegistries(manifestEntries);
            int targetRegistryIndex = -1;

            for (int i = 0; i < scopedRegistries.Count; ++i)
            {
                if (!string.Equals(scopedRegistries[i]?.url, registryUrl, StringComparison.Ordinal))
                {
                    continue;
                }

                targetRegistryIndex = i;
                break;
            }

            if (targetRegistryIndex < 0)
            {
                scopedRegistries.Add(new ScopedRegistryManifestModel
                {
                    name = registryName,
                    url = registryUrl,
                    scopes = Array.Empty<string>()
                });
                targetRegistryIndex = scopedRegistries.Count - 1;
            }

            var targetRegistry = scopedRegistries[targetRegistryIndex] ?? new ScopedRegistryManifestModel();
            targetRegistry.name = string.IsNullOrWhiteSpace(targetRegistry.name) ? registryName : targetRegistry.name;
            targetRegistry.url = string.IsNullOrWhiteSpace(targetRegistry.url) ? registryUrl : targetRegistry.url;

            var scopes = new List<string>(targetRegistry.scopes ?? Array.Empty<string>());
            bool hasScope = false;

            for (int i = 0; i < scopes.Count; ++i)
            {
                if (!string.Equals(scopes[i], scopeName, StringComparison.Ordinal))
                {
                    continue;
                }

                hasScope = true;
                break;
            }

            if (hasScope)
            {
                return false;
            }

            scopes.Add(scopeName);
            targetRegistry.scopes = scopes.ToArray();
            scopedRegistries[targetRegistryIndex] = targetRegistry;

            string scopedRegistriesJson = SerializeScopedRegistries(scopedRegistries);
            SetOrAddObjectEntry(manifestEntries, SCOPED_REGISTIRES, scopedRegistriesJson);

            File.WriteAllText(manifestPath, BuildJsonObjectText(manifestEntries));
            AssetDatabase.Refresh();
            return true;
        }

        internal static HashSet<string> GetInstalledScopeKeys()
        {
            var installedScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string manifestPath = GetManifestPath();

            if (!File.Exists(manifestPath))
            {
                return installedScopes;
            }

            string manifestJson = File.ReadAllText(manifestPath);

            if (!TryParseJsonObjectEntries(manifestJson, out var manifestEntries))
            {
                return installedScopes;
            }

            var scopedRegistries = GetScopedRegistries(manifestEntries);

            for (int i = 0; i < scopedRegistries.Count; ++i)
            {
                var registry = scopedRegistries[i];
                string registryUrl = registry?.url;

                if (string.IsNullOrWhiteSpace(registryUrl) || registry.scopes == null)
                {
                    continue;
                }

                for (int j = 0; j < registry.scopes.Length; ++j)
                {
                    string scopeName = registry.scopes[j];

                    if (string.IsNullOrWhiteSpace(scopeName))
                    {
                        continue;
                    }

                    installedScopes.Add(BuildScopeKey(registryUrl, scopeName));
                }
            }

            return installedScopes;
        }

        internal static Dictionary<string, string> GetInstalledPackagesById()
        {
            var installedPackages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string packagesLockPath = GetPackagesLockPath();

            if (!File.Exists(packagesLockPath))
            {
                return installedPackages;
            }

            string packagesLockJson = File.ReadAllText(packagesLockPath);

            if (!TryParseJsonObjectEntries(packagesLockJson, out var rootEntries)
                || !TryGetObjectEntryRawValue(rootEntries, DEPENDENCIES, out var dependenciesJson)
                || !TryParseJsonObjectEntries(dependenciesJson, out var dependencyEntries))
            {
                return installedPackages;
            }

            for (int i = 0; i < dependencyEntries.Count; ++i)
            {
                var dependencyEntry = dependencyEntries[i];

                if (string.IsNullOrWhiteSpace(dependencyEntry.Key)
                    || !TryParseJsonObjectEntries(dependencyEntry.RawValue, out var packageInfoEntries))
                {
                    continue;
                }

                if (!TryGetObjectEntryRawValue(packageInfoEntries, VERSION, out var versionJson)
                    || !TryReadJsonStringValue(versionJson, out var versionValue))
                {
                    versionValue = string.Empty;
                }

                installedPackages[dependencyEntry.Key] = versionValue ?? string.Empty;
            }

            return installedPackages;
        }

        internal static HashSet<string> GetManifestDependencyReferences()
        {
            var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string manifestPath = GetManifestPath();

            if (!File.Exists(manifestPath))
            {
                return references;
            }

            string manifestJson = File.ReadAllText(manifestPath);

            if (!TryParseJsonObjectEntries(manifestJson, out var rootEntries)
                || !TryGetObjectEntryRawValue(rootEntries, DEPENDENCIES, out var dependenciesJson)
                || !TryParseJsonObjectEntries(dependenciesJson, out var dependencyEntries))
            {
                return references;
            }

            for (int i = 0; i < dependencyEntries.Count; ++i)
            {
                if (!TryReadJsonStringValue(dependencyEntries[i].RawValue, out var packageReference)
                    || string.IsNullOrWhiteSpace(packageReference))
                {
                    continue;
                }

                references.Add(packageReference);
            }

            return references;
        }

        internal static HashSet<string> GetInstalledAssemblyNames()
        {
            var assemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string projectRootPath = GetProjectRootPath();

            var currentDomainAssemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int i = 0; i < currentDomainAssemblies.Length; ++i)
            {
                string assemblyLocation;

                try
                {
                    assemblyLocation = currentDomainAssemblies[i]?.Location;
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(assemblyLocation)
                    || !assemblyLocation.StartsWith(projectRootPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string assemblyName = currentDomainAssemblies[i]?.GetName()?.Name;

                if (string.IsNullOrWhiteSpace(assemblyName))
                {
                    continue;
                }

                assemblyNames.Add(assemblyName);
            }

            AddDllAssemblyNamesFromDirectory(Application.dataPath, assemblyNames);
            AddDllAssemblyNamesFromDirectory(Path.Combine(projectRootPath, "Packages"), assemblyNames);
            AddDllAssemblyNamesFromDirectory(Path.Combine(projectRootPath, "Library", "PackageCache"), assemblyNames);

            return assemblyNames;
        }

        internal static bool IsPackageReferenceInstalled(string packageReference,
            Dictionary<string, string> installedPackagesById, HashSet<string> manifestDependencyReferences,
            HashSet<string> installedAssemblyNames)
        {
            if (string.IsNullOrWhiteSpace(packageReference))
            {
                return false;
            }

            if (manifestDependencyReferences != null && manifestDependencyReferences.Contains(packageReference))
            {
                return true;
            }

            if (!TryParsePackageIdAndVersion(packageReference, out var packageId, out var requestedVersion))
            {
                return false;
            }

            if (installedPackagesById != null && installedPackagesById.TryGetValue(packageId, out var installedVersion))
            {
                if (string.IsNullOrWhiteSpace(requestedVersion))
                {
                    return true;
                }

                if (string.Equals(installedVersion, requestedVersion, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return IsPackageIdProvidedByAssemblies(packageId, installedAssemblyNames);
        }

        internal static bool TryParsePackageIdAndVersion(string packageReference, out string packageId, out string version)
        {
            packageId = null;
            version = null;

            if (string.IsNullOrWhiteSpace(packageReference))
            {
                return false;
            }

            if (packageReference.Contains("://", StringComparison.Ordinal)
                || packageReference.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int separatorIndex = packageReference.IndexOf('@');

            if (separatorIndex < 0)
            {
                packageId = packageReference;
                return true;
            }

            packageId = packageReference.Substring(0, separatorIndex);

            if (separatorIndex + 1 < packageReference.Length)
            {
                version = packageReference.Substring(separatorIndex + 1);
            }

            return true;
        }

        internal static string BuildScopeKey(string registryUrl, string scopeName)
        {
            return $"{registryUrl}|{scopeName}";
        }

        internal static bool IsScopeValid(ScopeModel scope)
        {
            return scope != null && IsScopeValid(scope.ScopeName, scope.RegistryUrl);
        }

        internal static bool IsScopeValid(string scopeName, string registryUrl)
        {
            return !string.IsNullOrWhiteSpace(scopeName)
                   && !string.IsNullOrWhiteSpace(registryUrl);
        }

        private static bool IsPackageIdProvidedByAssemblies(string packageId, HashSet<string> installedAssemblyNames)
        {
            if (installedAssemblyNames == null || installedAssemblyNames.Count == 0 || string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            var candidates = BuildNormalizedAssemblyCandidatesForPackageId(packageId);

            if (candidates.Count == 0)
            {
                return false;
            }

            foreach (var installedAssemblyName in installedAssemblyNames)
            {
                string normalizedAssemblyName = NormalizeKey(installedAssemblyName);

                if (string.IsNullOrWhiteSpace(normalizedAssemblyName))
                {
                    continue;
                }

                if (candidates.Contains(normalizedAssemblyName))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddDllAssemblyNamesFromDirectory(string rootPath, HashSet<string> assemblyNames)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                return;
            }

            foreach (var dllPath in Directory.EnumerateFiles(rootPath, "*.dll", SearchOption.AllDirectories))
            {
                string assemblyName = Path.GetFileNameWithoutExtension(dllPath);

                if (string.IsNullOrWhiteSpace(assemblyName))
                {
                    continue;
                }

                assemblyNames.Add(assemblyName);
            }
        }

        private static string GetProjectRootPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static HashSet<string> BuildNormalizedAssemblyCandidatesForPackageId(string packageId)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(packageId))
            {
                return candidates;
            }

            AddNormalizedCandidate(candidates, packageId);

            var parts = packageId.Split('.', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length > 0)
            {
                AddNormalizedCandidate(candidates, parts[^1]);
            }

            if (parts.Length > 1)
            {
                AddNormalizedCandidate(candidates, string.Join(string.Empty, parts, 1, parts.Length - 1));
            }

            if (parts.Length > 2)
            {
                AddNormalizedCandidate(candidates, string.Join(string.Empty, parts, 2, parts.Length - 2));
            }

            if (packageId.StartsWith(ORG_NUGET_PREFIX, StringComparison.OrdinalIgnoreCase))
            {
                AddNormalizedCandidate(candidates, packageId.Substring(ORG_NUGET_PREFIX.Length));
            }

            return candidates;
        }

        private static void AddNormalizedCandidate(HashSet<string> candidates, string value)
        {
            string normalizedValue = NormalizeKey(value);

            if (string.IsNullOrWhiteSpace(normalizedValue))
            {
                return;
            }

            candidates.Add(normalizedValue);
        }

        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);

            for (int i = 0; i < value.Length; ++i)
            {
                char c = value[i];

                if (!char.IsLetterOrDigit(c))
                {
                    continue;
                }

                builder.Append(char.ToLowerInvariant(c));
            }

            return builder.ToString();
        }

        private static string GetManifestPath()
        {
            string projectRootPath = GetProjectRootPath();
            return Path.Combine(projectRootPath, "Packages", "manifest.json");
        }

        private static string GetPackagesLockPath()
        {
            string projectRootPath = GetProjectRootPath();
            return Path.Combine(projectRootPath, "Packages", "packages-lock.json");
        }

        private static List<ScopedRegistryManifestModel> GetScopedRegistries(List<JsonObjectEntry> rootEntries)
        {
            var registries = new List<ScopedRegistryManifestModel>();

            if (!TryGetObjectEntryRawValue(rootEntries, SCOPED_REGISTIRES, out var scopedRegistriesJson))
            {
                return registries;
            }

            var wrapper = new ScopedRegistriesWrapper();
            string wrapperJson = "{\"items\":" + scopedRegistriesJson + "}";

            try
            {
                EditorJsonUtility.FromJsonOverwrite(wrapperJson, wrapper);
            }
            catch
            {
                return registries;
            }

            if (wrapper.items == null)
            {
                return registries;
            }

            registries.AddRange(wrapper.items);
            return registries;
        }

        private static string SerializeScopedRegistries(List<ScopedRegistryManifestModel> scopedRegistries)
        {
            var wrapper = new ScopedRegistriesWrapper
            {
                items = scopedRegistries?.ToArray() ?? Array.Empty<ScopedRegistryManifestModel>()
            };

            string wrapperJson = EditorJsonUtility.ToJson(wrapper, true);

            if (TryParseJsonObjectEntries(wrapperJson, out var wrapperEntries)
                && TryGetObjectEntryRawValue(wrapperEntries, "items", out var itemsJson))
            {
                return itemsJson;
            }

            return "[]";
        }

        private static void SetOrAddObjectEntry(List<JsonObjectEntry> entries, string key, string rawValue)
        {
            for (int i = 0; i < entries.Count; ++i)
            {
                if (!string.Equals(entries[i].Key, key, StringComparison.Ordinal))
                {
                    continue;
                }

                entries[i] = new JsonObjectEntry(key, rawValue);
                return;
            }

            entries.Add(new JsonObjectEntry(key, rawValue));
        }

        private static bool TryGetObjectEntryRawValue(List<JsonObjectEntry> entries, string key, out string rawValue)
        {
            for (int i = 0; i < entries.Count; ++i)
            {
                if (!string.Equals(entries[i].Key, key, StringComparison.Ordinal))
                {
                    continue;
                }

                rawValue = entries[i].RawValue;
                return true;
            }

            rawValue = null;
            return false;
        }

        private static string BuildJsonObjectText(List<JsonObjectEntry> entries)
        {
            var builder = new StringBuilder();
            builder.Append("{\n");

            for (int i = 0; i < entries.Count; ++i)
            {
                builder.Append("  \"");
                builder.Append(EscapeJsonString(entries[i].Key));
                builder.Append("\": ");
                builder.Append(entries[i].RawValue);

                if (i + 1 < entries.Count)
                {
                    builder.Append(',');
                }

                builder.Append('\n');
            }

            builder.Append('}');
            builder.Append('\n');
            return builder.ToString();
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length + 8);

            for (int i = 0; i < value.Length; ++i)
            {
                char c = value[i];

                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }

            return builder.ToString();
        }

        private static bool TryReadJsonStringValue(string valueJson, out string value)
        {
            value = null;

            if (string.IsNullOrWhiteSpace(valueJson))
            {
                return false;
            }

            int index = SkipWhitespace(valueJson, 0);

            if (!TryReadJsonString(valueJson, ref index, out value))
            {
                return false;
            }

            index = SkipWhitespace(valueJson, index);
            return index == valueJson.Length;
        }

        private static bool TryParseJsonObjectEntries(string json, out List<JsonObjectEntry> entries)
        {
            entries = new List<JsonObjectEntry>();

            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            int index = SkipWhitespace(json, 0);

            if (index >= json.Length || json[index] != '{')
            {
                return false;
            }

            ++index;

            while (true)
            {
                index = SkipWhitespace(json, index);

                if (index >= json.Length)
                {
                    return false;
                }

                if (json[index] == '}')
                {
                    ++index;
                    break;
                }

                if (!TryReadJsonString(json, ref index, out var key))
                {
                    return false;
                }

                index = SkipWhitespace(json, index);

                if (index >= json.Length || json[index] != ':')
                {
                    return false;
                }

                ++index;
                index = SkipWhitespace(json, index);

                int valueStart = index;

                if (!TryReadJsonValue(json, ref index))
                {
                    return false;
                }

                int valueEnd = index;

                entries.Add(new JsonObjectEntry(key, json.Substring(valueStart, valueEnd - valueStart).Trim()));

                index = SkipWhitespace(json, index);

                if (index >= json.Length)
                {
                    return false;
                }

                if (json[index] == ',')
                {
                    ++index;
                    continue;
                }

                if (json[index] == '}')
                {
                    ++index;
                    break;
                }

                return false;
            }

            index = SkipWhitespace(json, index);
            return index == json.Length;
        }

        private static bool TryReadJsonValue(string json, ref int index)
        {
            if (index >= json.Length)
            {
                return false;
            }

            char c = json[index];

            if (c == '"')
            {
                return TryReadJsonString(json, ref index, out _);
            }

            if (c == '{' || c == '[')
            {
                return TryReadJsonComposite(json, ref index);
            }

            int startIndex = index;

            while (index < json.Length)
            {
                c = json[index];

                if (c == ',' || c == '}' || c == ']')
                {
                    break;
                }

                ++index;
            }

            if (startIndex == index)
            {
                return false;
            }

            int endIndex = index;

            while (endIndex > startIndex && char.IsWhiteSpace(json[endIndex - 1]))
            {
                --endIndex;
            }

            return endIndex > startIndex;
        }

        private static bool TryReadJsonComposite(string json, ref int index)
        {
            int depth = 0;
            bool inString = false;
            bool isEscaped = false;

            while (index < json.Length)
            {
                char c = json[index++];

                if (inString)
                {
                    if (isEscaped)
                    {
                        isEscaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        isEscaped = true;
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{' || c == '[')
                {
                    ++depth;
                    continue;
                }

                if (c == '}' || c == ']')
                {
                    --depth;

                    if (depth == 0)
                    {
                        return true;
                    }

                    if (depth < 0)
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        private static bool TryReadJsonString(string json, ref int index, out string value)
        {
            value = null;

            if (index >= json.Length || json[index] != '"')
            {
                return false;
            }

            ++index;
            var builder = new StringBuilder();

            while (index < json.Length)
            {
                char c = json[index++];

                if (c == '"')
                {
                    value = builder.ToString();
                    return true;
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (index >= json.Length)
                {
                    return false;
                }

                char escaped = json[index++];

                switch (escaped)
                {
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '/':
                        builder.Append('/');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        if (!TryReadUnicodeCodepoint(json, ref index, out var unicodeChar))
                        {
                            return false;
                        }

                        builder.Append(unicodeChar);
                        break;
                    default:
                        return false;
                }
            }

            return false;
        }

        private static bool TryReadUnicodeCodepoint(string json, ref int index, out char c)
        {
            c = default;

            if (index + 3 >= json.Length)
            {
                return false;
            }

            int code = 0;

            for (int i = 0; i < 4; ++i)
            {
                code <<= 4;
                char h = json[index++];

                if (h >= '0' && h <= '9')
                {
                    code |= h - '0';
                    continue;
                }

                if (h >= 'a' && h <= 'f')
                {
                    code |= h - 'a' + 10;
                    continue;
                }

                if (h >= 'A' && h <= 'F')
                {
                    code |= h - 'A' + 10;
                    continue;
                }

                return false;
            }

            c = (char)code;
            return true;
        }

        private static int SkipWhitespace(string text, int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                ++index;
            }

            return index;
        }

        [Serializable]
        private sealed class RequiredPackagesRootModel
        {
            public PackageModel[] list;
        }

        [Serializable]
        private sealed class ScopedRegistriesWrapper
        {
            public ScopedRegistryManifestModel[] items;
        }

        [Serializable]
        private sealed class ScopedRegistryManifestModel
        {
            public string name;
            public string url;
            public string[] scopes;
        }

        private readonly struct JsonObjectEntry
        {
            internal readonly string Key;
            internal readonly string RawValue;

            internal JsonObjectEntry(string key, string rawValue)
            {
                Key = key;
                RawValue = rawValue;
            }
        }
    }
}
