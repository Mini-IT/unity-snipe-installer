using MiniIT.SnipeInstaller.Editor.Interfaces;
using MiniIT.SnipeInstaller.Editor.Models;
using MiniIT.SnipeInstaller.Editor.Utils;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using System;

namespace MiniIT.SnipeInstaller.Editor
{
    public sealed class SnipeInstallerWindowPresenter : ISnipeInstallerWindowPresenter
    {
        private static SnipeInstallerWindowPresenter _installCompletedEventOwner;

        private ISnipeInstallerWindowView _view;

        public SnipeInstallerWindowPresenter()
        {
            SubscribeToInstallCompletedEvent();
        }

        public void Show()
        {
            AttachView(EditorWindow.GetWindow<SnipeInstallerWindowView>());
            _view.ShowWindow();

            if (_view.IsViewReady)
            {
                InitializeView();
            }
        }

        private void AttachView(ISnipeInstallerWindowView view)
        {
            if (ReferenceEquals(_view, view))
            {
                return;
            }

            if (_view != null)
            {
                _view.ViewReady -= OnViewReady;
            }

            _view = view;
            _view.ViewReady -= OnViewReady;
            _view.ViewReady += OnViewReady;
        }

        private void OnViewReady()
        {
            InitializeView();
        }

        private void InitializeView()
        {
            BindData();
            AddListeners();
        }

        private void BindData()
        {
            if (_view == null)
            {
                return;
            }

            var packages = SnipeInstallerUtils.GetRequiredPackages();
            var installedScopeKeys = SnipeInstallerUtils.GetInstalledScopeKeys();
            var installedPackagesById = SnipeInstallerUtils.GetInstalledPackagesById();
            var manifestDependencyReferences = SnipeInstallerUtils.GetManifestDependencyReferences();
            var installedAssemblyNames = SnipeInstallerUtils.GetInstalledAssemblyNames();

            bool hasScopesToInstall;
            bool hasPackagesToInstall;

            _view.SetScopesItems(BuildScopeItems(packages, installedScopeKeys, out hasScopesToInstall));
            _view.SetPackagesItems(BuildPackageItems(packages, installedPackagesById, manifestDependencyReferences,
                installedAssemblyNames, out hasPackagesToInstall));

            if (_view.InstallScopesButton != null)
            {
                _view.InstallScopesButton.SetEnabled(hasScopesToInstall);
            }

            if (_view.InstallPackagesButton != null)
            {
                _view.InstallPackagesButton.SetEnabled(hasPackagesToInstall && !SnipeInstaller.IsPackagesInstalling());
            }

            UpdatePackagesInstallStatus();
        }

        private void AddListeners()
        {
            if (_view.InstallScopesButton != null)
            {
                _view.InstallScopesButton.clicked -= OnInstallScopesButtonClicked;
                _view.InstallScopesButton.clicked += OnInstallScopesButtonClicked;
            }

            if (_view.InstallPackagesButton != null)
            {
                _view.InstallPackagesButton.clicked -= OnInstallPackagesButtonClicked;
                _view.InstallPackagesButton.clicked += OnInstallPackagesButtonClicked;
            }

            if (_view.ResetPrefsButton != null)
            {
                _view.ResetPrefsButton.clicked -= OnResetPrefsButtonClicked;
                _view.ResetPrefsButton.clicked += OnResetPrefsButtonClicked;
            }
        }

        private static IReadOnlyList<string> BuildScopeItems(PackageModel[] packages, HashSet<string> installedScopeKeys,
            out bool hasScopesToInstall)
        {
            if (packages == null || packages.Length == 0)
            {
                hasScopesToInstall = false;
                return new List<string> { "All scopes already installed" };
            }

            var uniqueScopes = new HashSet<string>();

            for (int i = 0; i < packages.Length; ++i)
            {
                var scope = packages[i]?.Scope;

                if (scope == null)
                {
                    continue;
                }

                var scopeKey = SnipeInstallerUtils.BuildScopeKey(scope.RegistryUrl, scope.ScopeName);

                if (installedScopeKeys.Contains(scopeKey))
                {
                    continue;
                }

                uniqueScopes.Add($"{scope.ScopeName} -> {scope.RegistryName} ({scope.RegistryUrl})");
            }

            if (uniqueScopes.Count == 0)
            {
                hasScopesToInstall = false;
                return new List<string> { "All scopes already installed" };
            }

            hasScopesToInstall = true;
            return new List<string>(uniqueScopes);
        }

        private static IReadOnlyList<string> BuildPackageItems(PackageModel[] packages,
            Dictionary<string, string> installedPackagesById, HashSet<string> manifestDependencyReferences,
            HashSet<string> installedAssemblyNames, out bool hasPackagesToInstall)
        {
            if (packages == null || packages.Length == 0)
            {
                hasPackagesToInstall = false;
                return new List<string> { "All packages already installed" };
            }

            var packageItems = new List<string>(packages.Length);
            var uniquePendingReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < packages.Length; ++i)
            {
                var package = packages[i];

                if (package == null || string.IsNullOrWhiteSpace(package.Name))
                {
                    continue;
                }

                bool hasPendingEntries = false;
                var itemBuilder = new StringBuilder();

                if (TryAddPendingReference(package.Name, uniquePendingReferences, installedPackagesById,
                        manifestDependencyReferences, installedAssemblyNames))
                {
                    itemBuilder.Append(package.Name);
                    hasPendingEntries = true;
                }

                if (package.Dependencies != null && package.Dependencies.Length > 0)
                {
                    for (int j = 0; j < package.Dependencies.Length; ++j)
                    {
                        var dependency = package.Dependencies[j];

                        if (string.IsNullOrWhiteSpace(dependency))
                        {
                            continue;
                        }

                        if (!TryAddPendingReference(dependency, uniquePendingReferences, installedPackagesById,
                                manifestDependencyReferences, installedAssemblyNames))
                        {
                            continue;
                        }

                        if (hasPendingEntries)
                        {
                            itemBuilder.Append('\n').Append("  - ").Append(dependency);
                        }
                        else
                        {
                            itemBuilder.Append(dependency);
                        }

                        hasPendingEntries = true;
                    }
                }

                if (hasPendingEntries)
                {
                    packageItems.Add(itemBuilder.ToString());
                }
            }

            if (packageItems.Count == 0)
            {
                hasPackagesToInstall = false;
                packageItems.Add("All packages already installed");
                return packageItems;
            }

            hasPackagesToInstall = true;
            return packageItems;
        }

        private void OnInstallScopesButtonClicked()
        {
            SnipeInstaller.AddRequiredScopeRegistries();
            BindData();
        }

        private void OnInstallPackagesButtonClicked()
        {
            if (_view.InstallPackagesButton != null)
            {
                _view.InstallPackagesButton.SetEnabled(false);
            }

            _view.SetPackagesInstallStatus("Preparing package installation...", string.Empty, true);
            SnipeInstaller.AddRequiredPackages();
        }

        private void OnResetPrefsButtonClicked()
        {
            SnipeInstaller.ResetAllEditorPrefs();
            BindData();
        }

        private void OnRequiredPackagesInstallCompleted(bool _)
        {
            EditorApplication.delayCall -= BindData;
            EditorApplication.delayCall += BindData;
        }

        private void OnRequiredPackagesInstallProgressChanged(string currentPackageName, int remainingPackagesCount,
            int totalPackagesCount)
        {
            if (_view == null)
            {
                return;
            }

            SetPackagesInstallStatus(currentPackageName, remainingPackagesCount, totalPackagesCount);

            if (_view.InstallPackagesButton != null)
            {
                _view.InstallPackagesButton.SetEnabled(false);
            }
        }

        private void SubscribeToInstallCompletedEvent()
        {
            if (_installCompletedEventOwner != null && !ReferenceEquals(_installCompletedEventOwner, this))
            {
                SnipeInstaller.RequiredPackagesInstallCompleted -=
                    _installCompletedEventOwner.OnRequiredPackagesInstallCompleted;
                SnipeInstaller.RequiredPackagesInstallProgressChanged -=
                    _installCompletedEventOwner.OnRequiredPackagesInstallProgressChanged;
            }

            SnipeInstaller.RequiredPackagesInstallCompleted -= OnRequiredPackagesInstallCompleted;
            SnipeInstaller.RequiredPackagesInstallCompleted += OnRequiredPackagesInstallCompleted;
            SnipeInstaller.RequiredPackagesInstallProgressChanged -= OnRequiredPackagesInstallProgressChanged;
            SnipeInstaller.RequiredPackagesInstallProgressChanged += OnRequiredPackagesInstallProgressChanged;

            _installCompletedEventOwner = this;
        }

        private void UpdatePackagesInstallStatus()
        {
            if (!SnipeInstaller.IsPackagesInstalling())
            {
                _view.SetPackagesInstallStatus(string.Empty, string.Empty, false);
                return;
            }

            if (!SnipeInstaller.TryGetPackagesInstallProgress(out var currentPackageName, out var remainingPackagesCount,
                    out var totalPackagesCount))
            {
                _view.SetPackagesInstallStatus("Preparing package installation...", string.Empty, true);
                return;
            }

            SetPackagesInstallStatus(currentPackageName, remainingPackagesCount, totalPackagesCount);
        }

        private void SetPackagesInstallStatus(string currentPackageName, int remainingPackagesCount, int totalPackagesCount)
        {
            if (remainingPackagesCount < 0)
            {
                _view.SetPackagesInstallStatus("Preparing package installation...", string.Empty, true);
                return;
            }

            int leftPackagesCount = string.IsNullOrWhiteSpace(currentPackageName)
                ? remainingPackagesCount
                : remainingPackagesCount + 1;

            string statusText = totalPackagesCount > 0
                ? $"Installing packages... ({leftPackagesCount} left of {totalPackagesCount})"
                : $"Installing packages... ({leftPackagesCount} left)";

            string currentPackageText = string.IsNullOrWhiteSpace(currentPackageName)
                ? string.Empty
                : $"Current package: {currentPackageName}";

            _view.SetPackagesInstallStatus(statusText, currentPackageText, true);
        }

        private static bool TryAddPendingReference(string packageReference, HashSet<string> uniquePendingReferences,
            Dictionary<string, string> installedPackagesById, HashSet<string> manifestDependencyReferences,
            HashSet<string> installedAssemblyNames)
        {
            if (string.IsNullOrWhiteSpace(packageReference))
            {
                return false;
            }

            if (SnipeInstallerUtils.IsPackageReferenceInstalled(packageReference, installedPackagesById,
                    manifestDependencyReferences, installedAssemblyNames))
            {
                return false;
            }

            return uniquePendingReferences.Add(packageReference);
        }
    }
}
