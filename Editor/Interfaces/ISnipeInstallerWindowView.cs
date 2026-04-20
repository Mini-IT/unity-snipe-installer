using System.Collections.Generic;
using UnityEngine.UIElements;
using System;

namespace MiniIT.SnipeInstaller.Editor.Interfaces
{
    public interface ISnipeInstallerWindowView
    {
        Button InstallScopesButton { get; }
        Button InstallPackagesButton { get; }
        Button ResetPrefsButton { get; }
        bool IsViewReady { get; }
        event Action ViewReady;

        void ShowWindow();
        void SetScopesItems(IReadOnlyList<string> items);
        void SetPackagesItems(IReadOnlyList<string> items);
        void SetPackagesInstallStatus(string statusText, string currentPackageText, bool isVisible);
    }
}
