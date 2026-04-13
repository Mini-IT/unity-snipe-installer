using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

public static class InstallSnipePackages
{
    private const string LOG_PREFIX = "Snipe Installer:";
    private const string INSTALLER_ASSET_PATH = "Assets/Editor/InstallSnipePackages.cs";

    private const string REGISTRY_NAME = "OpenUPM";
    private const string REGISTRY_URL = "https://package.openupm.com";
    private const string REGISTRY_SCOPE = "org.nuget";

    private const string SESSION_STATE_DONE = "SnipeInstaller/Done";
    private const string SESSION_STATE_REGISTRY_DONE = "SnipeInstaller/RegistryDone";
    private const string SESSION_STATE_PACKAGE_INDEX = "SnipeInstaller/PackageIndex";

    private const int DELAY_MS = 100;

    private static bool s_running;

    private static readonly PackageToInstall[] Packages =
    {
        new PackageToInstall("com.cysharp.unitask", "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask"),
        new PackageToInstall("com.miniit.fastjson", "https://github.com/Mini-IT/fastJSON-unity-package.git"),
        new PackageToInstall("com.miniit.sharedprefs", "https://github.com/Mini-IT/unity-sharedprefs.git"),
        new PackageToInstall("com.miniit.altertask", "https://github.com/Mini-IT/unity-altertask.git"),
        new PackageToInstall("com.miniit.framework.streamingassetsreader", "https://github.com/Mini-IT/unity-framework-streamingassetsreader.git"),
        new PackageToInstall("com.miniit.framework.utilities", "https://github.com/Mini-IT/unity-framework-utilities.git"),

        new PackageToInstall("org.nuget.microsoft.extensions.logging", "org.nuget.microsoft.extensions.logging"),
        new PackageToInstall("com.miniit.logger", "https://github.com/Mini-IT/unity-logger.git"),
        new PackageToInstall("com.miniit.snipe.tools", "https://github.com/Mini-IT/SnipeToolsUnityPackage.git"),
        new PackageToInstall("com.miniit.snipe.client", "https://github.com/Mini-IT/SnipeUnityPackage.git")
    };

    [InitializeOnLoadMethod]
    private static void OnLoad()
    {
        if (SessionState.GetBool(SESSION_STATE_DONE, false))
        {
            return;
        }

        EditorApplication.delayCall += Run;
    }

    private static async void Run()
    {
        if (s_running)
        {
            return;
        }

        s_running = true;

        try
        {
            if (!SessionState.GetBool(SESSION_STATE_REGISTRY_DONE, false))
            {
                await EnsureOpenUpmRegistry();
                SessionState.SetBool(SESSION_STATE_REGISTRY_DONE, true);
            }

            HashSet<string> installedPackages = await GetInstalledPackages();
            await InstallPackages(installedPackages);
            await Finish();
        }
        catch (Exception e)
        {
            Debug.LogError($"{LOG_PREFIX} exception {e}");
            s_running = false;
        }
    }

    private static async Task EnsureOpenUpmRegistry()
    {
        if (await CanFindOpenUpmPackage())
        {
            Debug.Log($"{LOG_PREFIX} registry already added");
            return;
        }

        await AddOpenUpmRegistry();
    }

    private static async Task<bool> CanFindOpenUpmPackage()
    {
        SearchRequest request = Client.Search("org.nuget.microsoft.extensions.logging");
        await Wait(request);

        return request.Status == StatusCode.Success
            && request.Result != null
            && request.Result.Length > 0;
    }

    private static async Task AddOpenUpmRegistry()
    {
        BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;

#if UNITY_6000_0_OR_NEWER
        Type[] signature = { typeof(string), typeof(string), typeof(string[]), typeof(bool) };
        object[] args = { REGISTRY_NAME, REGISTRY_URL, new[] { REGISTRY_SCOPE }, false };
#else
        Type[] signature = { typeof(string), typeof(string), typeof(string[]) };
        object[] args = { REGISTRY_NAME, REGISTRY_URL, new[] { REGISTRY_SCOPE } };
#endif

        MethodInfo method = typeof(Client).GetMethod("AddScopedRegistry", flags, null, signature, null);
        if (method == null)
        {
            Debug.LogError($"{LOG_PREFIX} Client.AddScopedRegistry was not found.");
            return;
        }

        Request<RegistryInfo> request = (Request<RegistryInfo>)method.Invoke(null, args);
        await Wait(request);

        if (request.Status == StatusCode.Failure)
        {
            Debug.LogWarning($"{LOG_PREFIX} registry add returned: {request.Error?.message}");
        }
        else
        {
            Debug.Log($"{LOG_PREFIX} registry added");
        }
    }

    private static async Task<HashSet<string>> GetInstalledPackages()
    {
        HashSet<string> installedPackages = new HashSet<string>();

        ListRequest request = Client.List(true, true);
        await Wait(request);

        if (request.Status == StatusCode.Failure)
        {
            Debug.LogWarning($"{LOG_PREFIX} package list failed: {request.Error?.message}");
            return installedPackages;
        }

        foreach (UnityEditor.PackageManager.PackageInfo package in request.Result)
        {
            installedPackages.Add(package.name);
        }

        return installedPackages;
    }

    private static async Task InstallPackages(HashSet<string> installedPackages)
    {
        int index = SessionState.GetInt(SESSION_STATE_PACKAGE_INDEX, 0);

        for (; index < Packages.Length; index++)
        {
            PackageToInstall package = Packages[index];

            if (installedPackages.Contains(package.Name))
            {
                Debug.Log($"{LOG_PREFIX} ({index + 1} / {Packages.Length}) already installed {package.Name}");
                SessionState.SetInt(SESSION_STATE_PACKAGE_INDEX, index + 1);
                continue;
            }

            Debug.Log($"{LOG_PREFIX} ({index + 1} / {Packages.Length}) installing {package.Id}");

            AddRequest request = Client.Add(package.Id);
            await Wait(request);

            if (request.Status == StatusCode.Failure)
            {
                Debug.LogError($"{LOG_PREFIX} add failed for '{package.Id}': {request.Error?.message}");
                return;
            }

            Debug.Log($"{LOG_PREFIX} installed {request.Result.packageId}");

            installedPackages.Add(package.Name);
            SessionState.SetInt(SESSION_STATE_PACKAGE_INDEX, index + 1);
        }
    }

    private static async Task Wait(Request request)
    {
        while (!request.IsCompleted)
        {
            await Task.Delay(DELAY_MS);
        }
    }

    private static async Task Finish()
    {
        while (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            Debug.Log($"{LOG_PREFIX} waiting for compilation");
            await Task.Delay(DELAY_MS);
        }

        SessionState.SetBool(SESSION_STATE_DONE, true);
        SessionState.EraseBool(SESSION_STATE_REGISTRY_DONE);
        SessionState.EraseInt(SESSION_STATE_PACKAGE_INDEX);

        if (AssetDatabase.DeleteAsset(INSTALLER_ASSET_PATH))
        {
            Debug.Log($"{LOG_PREFIX} deleted installer");
        }
        else
        {
            Debug.LogWarning($"{LOG_PREFIX} could not delete {INSTALLER_ASSET_PATH}");
        }
    }

    private readonly struct PackageToInstall
    {
        public readonly string Name;
        public readonly string Id;

        public PackageToInstall(string name, string id)
        {
            Name = name;
            Id = id;
        }
    }
}
