using MiniIT.SnipeInstaller.Editor.Interfaces;
using UnityEditor;

namespace MiniIT.SnipeInstaller.Editor
{
    [InitializeOnLoad]
    public static class SnipeInstallerEditorHandler
    {
        private static ISnipeInstallerWindowPresenter _presenter;

        static SnipeInstallerEditorHandler()
        {
            EditorApplication.delayCall += RunOnceOnLoad;
        }

        private static void RunOnceOnLoad()
        {
            if (SnipeInstaller.AllPackagesIsInstalled())
            {
                return;
            }

            if (SnipeInstaller.IsPackagesInstallPendingResume())
            {
                SnipeInstaller.AddRequiredPackages();
            }

            ShowWindowFromTools();
        }

        [MenuItem("Tools/Snipe Installer")]
        private static void ShowWindowFromTools()
        {
            _presenter ??= new SnipeInstallerWindowPresenter();
            _presenter.Show();
        }
    }
}
