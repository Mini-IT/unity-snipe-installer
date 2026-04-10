using System.Text.Json.Serialization.Metadata;
using MiniIT.SnipeInstaller.Editor.Models;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json;
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
        private const string SCOPES = "scopes";
        private const string DEPENDENCIES = "dependencies";
        private const string VERSION = "version";
        private const string NAME = "name";
        private const string URL = "url";
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

            var root = JsonNode.Parse(packagesTextAsset.text);
            var listNode = root?["list"];

            if (listNode == null)
            {
                return Array.Empty<PackageModel>();
            }

            return JsonSerializer.Deserialize<PackageModel[]>(listNode.ToJsonString(), new JsonSerializerOptions
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                PropertyNameCaseInsensitive = true,
                IncludeFields = true
            }) ?? Array.Empty<PackageModel>();
        }

        internal static bool TryAddScopeRegistry(string scopeName, string registryName, string registryUrl)
        {
            string manifestPath = GetManifestPath();

            var root = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
                       ?? new JsonObject();
            var scopedRegistries = root[SCOPED_REGISTIRES] as JsonArray
                                   ?? new JsonArray();

            root[SCOPED_REGISTIRES] = scopedRegistries;

            JsonObject targetRegistry = default;

            foreach (var node in scopedRegistries)
            {
                var jsonObject = node as JsonObject;

                if (jsonObject == null)
                {
                    continue;
                }

                if ((string)jsonObject[URL] == registryUrl)
                {
                    targetRegistry = jsonObject;
                    break;
                }
            }

            if (targetRegistry == null)
            {
                targetRegistry = new JsonObject
                {
                    [NAME] = registryName,
                    [URL] = registryUrl,
                    [SCOPES] = new JsonArray()
                };

                scopedRegistries.Add(targetRegistry);
            }

            var scopes = targetRegistry[SCOPES] as JsonArray
                         ?? new JsonArray();

            targetRegistry[SCOPES] = scopes;

            bool hasScope = default;

            foreach (var scope in scopes)
            {
                if ((string)scope == scopeName)
                {
                    hasScope = true;
                    break;
                }
            }

            if (!hasScope)
            {
                scopes.Add(scopeName);

                File.WriteAllText(manifestPath, root.ToJsonString(new JsonSerializerOptions
                {
                    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                    WriteIndented = true
                }));
                AssetDatabase.Refresh();
            }

            return !hasScope;
        }

        internal static HashSet<string> GetInstalledScopeKeys()
        {
            var installedScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string manifestPath = GetManifestPath();

            if (!File.Exists(manifestPath))
            {
                return installedScopes;
            }

            var root = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject();

            if (root == null)
            {
                return installedScopes;
            }

            var scopedRegistries = root[SCOPED_REGISTIRES] as JsonArray;

            if (scopedRegistries == null)
            {
                return installedScopes;
            }

            foreach (var registryNode in scopedRegistries)
            {
                var registry = registryNode as JsonObject;

                if (registry == null)
                {
                    continue;
                }

                var registryUrl = (string)registry[URL];

                if (string.IsNullOrWhiteSpace(registryUrl))
                {
                    continue;
                }

                var scopes = registry[SCOPES] as JsonArray;

                if (scopes == null)
                {
                    continue;
                }

                foreach (var scopeNode in scopes)
                {
                    string scopeName = (string)scopeNode;

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

            var root = JsonNode.Parse(File.ReadAllText(packagesLockPath))?.AsObject();

            if (root == null)
            {
                return installedPackages;
            }

            var dependencies = root[DEPENDENCIES] as JsonObject;

            if (dependencies == null)
            {
                return installedPackages;
            }

            foreach (var dependency in dependencies)
            {
                string packageId = dependency.Key;
                var packageInfo = dependency.Value as JsonObject;

                if (string.IsNullOrWhiteSpace(packageId) || packageInfo == null)
                {
                    continue;
                }

                installedPackages[packageId] = (string)packageInfo[VERSION] ?? string.Empty;
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

            var root = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject();

            if (root == null)
            {
                return references;
            }

            var dependencies = root[DEPENDENCIES] as JsonObject;

            if (dependencies == null)
            {
                return references;
            }

            foreach (var dependency in dependencies)
            {
                string packageReference = (string)dependency.Value;

                if (string.IsNullOrWhiteSpace(packageReference))
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
    }
}
