using MiniIT.SnipeInstaller.Editor.Models;
using UnityEditor.PackageManager.Requests;
using MiniIT.SnipeInstaller.Editor.Utils;
using UnityEditor.PackageManager;
using System.Collections.Generic;
using UnityEditor;
using System;

namespace MiniIT.SnipeInstaller.Editor
{
    public static class SnipeInstaller
    {
        private const string SNIPE_PACKAGES_INSTALLED_PREFS_KEY = "MiniIT.SnipeInstaller.Packages";
        private const string SNIPE_PACKAGES_INSTALLING_PREFS_KEY = "MiniIT.SnipeInstaller.PackagesInstalling";
        private const string SNIPE_PACKAGES_CURRENT_PREFS_KEY = "MiniIT.SnipeInstaller.CurrentPackage";
        private const string SNIPE_PACKAGES_REMAINING_PREFS_KEY = "MiniIT.SnipeInstaller.RemainingPackages";
        private const string SNIPE_PACKAGES_TOTAL_PREFS_KEY = "MiniIT.SnipeInstaller.TotalPackages";
        private const string SNIPE_LEGACY_RUN_ONCE_PREFS_KEY = "MiniIT.SnipeInstaller.RunOnceOnLoad";
        public static event Action<bool> RequiredPackagesInstallCompleted;
        public static event Action<string, int, int> RequiredPackagesInstallProgressChanged;

        private static List<string> _failedPackageNamesList = new List<string>();
        private static Queue<string> _packageNamesQueue = new Queue<string>();
        private static AddRequest _addRequest;
        private static ListRequest _listRequest;

        private static bool _packagesInstallingInProcess;
        private static bool _hasErrorsWhileInstalling;
        private static string _currentPackageName;
        private static PackageModel[] _requestedPackages;
        private static int _totalPackagesCount;

        public static bool AllPackagesIsInstalled()
        {
            return EditorPrefs.GetString(SNIPE_PACKAGES_INSTALLED_PREFS_KEY, string.Empty)
                == SnipeInstallerUtils.GetPackagesHash() && !_packagesInstallingInProcess;
        }

        public static bool IsPackagesInstallPendingResume()
        {
            return EditorPrefs.GetBool(SNIPE_PACKAGES_INSTALLING_PREFS_KEY, false);
        }

        public static bool IsPackagesInstalling()
        {
            return _packagesInstallingInProcess || IsPackagesInstallPendingResume();
        }

        public static bool TryGetPackagesInstallProgress(out string currentPackageName, out int remainingPackagesCount,
            out int totalPackagesCount)
        {
            currentPackageName = EditorPrefs.GetString(SNIPE_PACKAGES_CURRENT_PREFS_KEY, string.Empty);
            remainingPackagesCount = EditorPrefs.GetInt(SNIPE_PACKAGES_REMAINING_PREFS_KEY, -1);
            totalPackagesCount = EditorPrefs.GetInt(SNIPE_PACKAGES_TOTAL_PREFS_KEY, -1);

            return IsPackagesInstallPendingResume();
        }

        public static void ResetAllEditorPrefs()
        {
            EditorApplication.update -= WaitList;
            EditorApplication.update -= WaitAdd;

            _listRequest = null;
            _addRequest = null;
            _packagesInstallingInProcess = false;
            _hasErrorsWhileInstalling = false;
            _currentPackageName = string.Empty;
            _requestedPackages = null;
            _totalPackagesCount = default;

            _failedPackageNamesList.Clear();
            _packageNamesQueue.Clear();

            EditorPrefs.DeleteKey(SNIPE_PACKAGES_INSTALLED_PREFS_KEY);
            EditorPrefs.DeleteKey(SNIPE_LEGACY_RUN_ONCE_PREFS_KEY);
            ClearTransientInstallPrefs();

            UnityEngine.Debug.Log("[SnipeInstaller] All installer EditorPrefs were reset.");
        }

        public static void AddRequiredScopeRegistries()
        {
            var packages = SnipeInstallerUtils.GetRequiredPackages();

            if (packages == null)
            {
                return;
            }

            int addedScopesCount = default;

            for (int i = 0; i < packages.Length; ++i)
            {
                var package = packages[i];

                if (package.Scope == null)
                {
                    continue;
                }

                if (!SnipeInstallerUtils.TryAddScopeRegistry(package.Scope.ScopeName,
                        package.Scope.RegistryName, package.Scope.RegistryUrl))
                {
                    continue;
                }

                ++addedScopesCount;
            }

            if (addedScopesCount > 0)
            {
                UnityEngine.Debug.Log(
                    $"[{nameof(SnipeInstaller)}.{nameof(AddRequiredScopeRegistries)}] Scopes count was added: {addedScopesCount}");
                
                Client.Resolve();
            }
        }

        public static void AddRequiredPackages()
        {
            if (_packagesInstallingInProcess)
            {
                return;
            }

            var packages = SnipeInstallerUtils.GetRequiredPackages();

            if (packages == null)
            {
                ClearTransientInstallPrefs();
                return;
            }

            _packagesInstallingInProcess = true;
            _hasErrorsWhileInstalling = false;
            EditorPrefs.SetBool(SNIPE_PACKAGES_INSTALLING_PREFS_KEY, true);
            SaveInstallProgress(string.Empty, -1, -1);

            _failedPackageNamesList.Clear();
            _packageNamesQueue.Clear();
            _requestedPackages = packages;
            _totalPackagesCount = default;

            _listRequest = Client.List(true, true);
            EditorApplication.update += WaitList;
        }

        private static void WaitList()
        {
            if (!_listRequest.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= WaitList;

            var installedPackagesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (_listRequest.Status == StatusCode.Success && _listRequest.Result != null)
            {
                foreach (var packageInfo in _listRequest.Result)
                {
                    if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.name))
                    {
                        continue;
                    }

                    installedPackagesById[packageInfo.name] = packageInfo.version ?? string.Empty;
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning(
                    $"[SnipeInstaller] Failed to read installed packages list. Fallback to install all from config. Error: {_listRequest.Error?.message}");
            }

            _listRequest = null;

            var manifestDependencyReferences = SnipeInstallerUtils.GetManifestDependencyReferences();
            var installedAssemblyNames = SnipeInstallerUtils.GetInstalledAssemblyNames();
            FillQueueForInstalling(_requestedPackages, installedPackagesById, manifestDependencyReferences,
                installedAssemblyNames);
            InstallNext();
        }

        private static void InstallNext()
        {
            if (_packageNamesQueue.Count == 0)
            {
                _packagesInstallingInProcess = false;
                bool installSucceeded = !_hasErrorsWhileInstalling;

                if (!installSucceeded)
                {
                    EditorPrefs.SetString(SNIPE_PACKAGES_INSTALLED_PREFS_KEY, string.Empty);
                    UnityEngine.Debug.LogError(
                        $"[SnipeInstaller] completed with errors: {string.Join(", ", _failedPackageNamesList)}");
                }
                else
                {
                    EditorPrefs.SetString(SNIPE_PACKAGES_INSTALLED_PREFS_KEY, SnipeInstallerUtils.GetPackagesHash());
                    UnityEngine.Debug.Log("[SnipeInstaller] all packages installed");
                }

                ClearTransientInstallPrefs();
                RequiredPackagesInstallCompleted?.Invoke(installSucceeded);
                return;
            }

            _currentPackageName = _packageNamesQueue.Dequeue();
            SaveInstallProgress(_currentPackageName, _packageNamesQueue.Count, _totalPackagesCount);
            RequiredPackagesInstallProgressChanged?.Invoke(_currentPackageName, _packageNamesQueue.Count,
                _totalPackagesCount);
            _addRequest = Client.Add(_currentPackageName);

            EditorApplication.update += WaitAdd;
        }

        private static void WaitAdd()
        {
            if (!_addRequest.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= WaitAdd;

            if (_addRequest.Status == StatusCode.Success)
            {
                UnityEngine.Debug.Log($"[SnipeInstaller] installed: {_currentPackageName}");
            }
            else
            {
                _hasErrorsWhileInstalling = true;
                _failedPackageNamesList.Add(_currentPackageName);

                UnityEngine.Debug.LogError(
                    $"[SnipeInstaller] failed: {_currentPackageName} | {_addRequest.Error?.message}");
            }

            _addRequest = null;

            InstallNext();
        }

        private static void FillQueueForInstalling(PackageModel[] packages,
            Dictionary<string, string> installedPackagesById, HashSet<string> manifestDependencyReferences,
            HashSet<string> installedAssemblyNames)
        {
            var uniquePackageNames = new HashSet<string>(packages.Length, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < packages.Length; ++i)
            {
                var package = packages[i];

                if (package == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(package.Name))
                {
                    continue;
                }

                if (package.Dependencies == null)
                {
                    TryEnqueue(package.Name, uniquePackageNames, installedPackagesById, manifestDependencyReferences,
                        installedAssemblyNames);
                    continue;
                }

                for (int j = 0; j < package.Dependencies.Length; ++j)
                {
                    TryEnqueue(package.Dependencies[j], uniquePackageNames, installedPackagesById,
                        manifestDependencyReferences, installedAssemblyNames);
                }

                TryEnqueue(package.Name, uniquePackageNames, installedPackagesById, manifestDependencyReferences,
                    installedAssemblyNames);
            }

            foreach (var packageName in uniquePackageNames)
            {
                _packageNamesQueue.Enqueue(packageName);
            }

            _totalPackagesCount = _packageNamesQueue.Count;

            static void TryEnqueue(string packageReference, HashSet<string> uniquePackageNames,
                Dictionary<string, string> installedPackagesById, HashSet<string> manifestDependencyReferences,
                HashSet<string> installedAssemblyNames)
            {
                if (string.IsNullOrWhiteSpace(packageReference))
                {
                    return;
                }

                if (SnipeInstallerUtils.IsPackageReferenceInstalled(packageReference, installedPackagesById,
                        manifestDependencyReferences, installedAssemblyNames))
                {
                    return;
                }

                uniquePackageNames.Add(packageReference);
            }
        }

        private static void SaveInstallProgress(string currentPackageName, int remainingPackagesCount,
            int totalPackagesCount)
        {
            EditorPrefs.SetString(SNIPE_PACKAGES_CURRENT_PREFS_KEY, currentPackageName ?? string.Empty);
            EditorPrefs.SetInt(SNIPE_PACKAGES_REMAINING_PREFS_KEY, remainingPackagesCount);
            EditorPrefs.SetInt(SNIPE_PACKAGES_TOTAL_PREFS_KEY, totalPackagesCount);
        }

        private static void ClearTransientInstallPrefs()
        {
            EditorPrefs.DeleteKey(SNIPE_PACKAGES_INSTALLING_PREFS_KEY);
            EditorPrefs.DeleteKey(SNIPE_PACKAGES_CURRENT_PREFS_KEY);
            EditorPrefs.DeleteKey(SNIPE_PACKAGES_REMAINING_PREFS_KEY);
            EditorPrefs.DeleteKey(SNIPE_PACKAGES_TOTAL_PREFS_KEY);
        }
    }
}
