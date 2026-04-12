using MiniIT.SnipeInstaller.Editor.Interfaces;
using System.Collections.Generic;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEngine;
using System;

namespace MiniIT.SnipeInstaller.Editor
{
    public sealed class SnipeInstallerWindowView : EditorWindow, ISnipeInstallerWindowView
    {
        public Button InstallScopesButton { get; private set; }
        public Button InstallPackagesButton { get; private set; }
        public Button ResetPrefsButton { get; private set; }

        public bool IsViewReady { get; private set; }

        public event Action ViewReady;

        private ScrollView _scopesListView;
        private ScrollView _packagesListView;
        private VisualElement _packagesInstallStatusContainer;
        private Label _packagesInstallStatusLabel;
        private Label _currentPackageLabel;

        public void ShowWindow()
        {
            minSize = new Vector2(640f, 520f);
            Show();
        }

        public void CreateGUI()
        {
            var uxml = Resources.Load<VisualTreeAsset>("UI/SnipeInstallerWindow");
            var uss = Resources.Load<StyleSheet>("UI/SnipeInstallerWindow");

            titleContent = new GUIContent("Snipe installer");

            uxml.CloneTree(rootVisualElement);
            rootVisualElement.styleSheets.Add(uss);

            _scopesListView = rootVisualElement.Q<ScrollView>("ScopesList");
            _packagesListView = rootVisualElement.Q<ScrollView>("PackagesList");
            _packagesInstallStatusContainer = rootVisualElement.Q<VisualElement>("PackagesInstallStatusContainer");
            _packagesInstallStatusLabel = rootVisualElement.Q<Label>("PackagesInstallStatusLabel");
            _currentPackageLabel = rootVisualElement.Q<Label>("CurrentPackageLabel");

            InstallScopesButton = rootVisualElement.Q<Button>("InstallScopesBtn");
            InstallPackagesButton = rootVisualElement.Q<Button>("InstallPackagesBtn");
            ResetPrefsButton = rootVisualElement.Q<Button>("ResetPrefsBtn");

            RegisterAdaptiveResizeHandlers();
            SetPackagesInstallStatus(string.Empty, string.Empty, false);

            IsViewReady = true;
            ViewReady?.Invoke();
        }

        public void SetScopesItems(IReadOnlyList<string> items)
        {
            BindList(_scopesListView, items, minHeight: 24f, maxHeight: 180f);
        }

        public void SetPackagesItems(IReadOnlyList<string> items)
        {
            BindList(_packagesListView, items, minHeight: 24f, maxHeight: 360f);
        }

        public void SetPackagesInstallStatus(string statusText, string currentPackageText, bool isVisible)
        {
            if (_packagesInstallStatusContainer == null)
            {
                return;
            }

            _packagesInstallStatusContainer.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;

            if (_packagesInstallStatusLabel != null)
            {
                _packagesInstallStatusLabel.text = statusText ?? string.Empty;
            }

            if (_currentPackageLabel != null)
            {
                _currentPackageLabel.style.display = string.IsNullOrWhiteSpace(currentPackageText)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
                _currentPackageLabel.text = currentPackageText ?? string.Empty;
            }
        }

        private void RegisterAdaptiveResizeHandlers()
        {
            if (_scopesListView != null)
            {
                RegisterAdaptiveResizeHandler(_scopesListView, minHeight: 24f, maxHeight: 180f);
            }

            if (_packagesListView != null)
            {
                RegisterAdaptiveResizeHandler(_packagesListView, minHeight: 24f, maxHeight: 360f);
            }
        }

        private static void RegisterAdaptiveResizeHandler(ScrollView scrollView, float minHeight, float maxHeight)
        {
            if (scrollView == null)
            {
                return;
            }

            scrollView.RegisterCallback<GeometryChangedEvent>(_ => ApplyAdaptiveHeight(scrollView, minHeight, maxHeight));
            scrollView.contentContainer.RegisterCallback<GeometryChangedEvent>(_ =>
                ApplyAdaptiveHeight(scrollView, minHeight, maxHeight));
        }

        private void BindList(ScrollView listView, IReadOnlyList<string> items, float minHeight, float maxHeight)
        {
            if (listView == null)
            {
                return;
            }

            var safeItems = items == null ? new List<string>() : new List<string>(items);
            listView.contentContainer.Clear();

            for (int i = 0; i < safeItems.Count; ++i)
            {
                AddItemLines(listView, safeItems[i]);
            }

            listView.scrollOffset = Vector2.zero;
            listView.schedule.Execute(() => ApplyAdaptiveHeight(listView, minHeight, maxHeight));
        }

        private static void AddItemLines(ScrollView listView, string itemText)
        {
            if (string.IsNullOrEmpty(itemText))
            {
                return;
            }

            var lines = itemText.Split('\n');

            for (int i = 0; i < lines.Length; ++i)
            {
                var label = new Label(lines[i]);
                label.AddToClassList("list-item");
                label.pickingMode = PickingMode.Ignore;
                listView.contentContainer.Add(label);
            }
        }

        private static void ApplyAdaptiveHeight(ScrollView scrollView, float minHeight, float maxHeight)
        {
            if (scrollView == null)
            {
                return;
            }

            float contentHeight = scrollView.contentContainer.layout.height;
            int lineCount = scrollView.contentContainer.childCount;
            float estimatedLineHeight = EditorGUIUtility.singleLineHeight + 6f;
            float fallbackHeight = lineCount > 0
                ? (lineCount * estimatedLineHeight) + 10f
                : minHeight;

            float measuredContentHeight = contentHeight > 0f ? contentHeight + 8f : fallbackHeight;
            float targetHeight = Mathf.Clamp(measuredContentHeight, minHeight, maxHeight);
            bool hasOverflow = measuredContentHeight > maxHeight + 0.5f;

            scrollView.style.height = targetHeight;
            scrollView.style.minHeight = minHeight;
            scrollView.style.maxHeight = maxHeight;
            scrollView.style.flexGrow = 0f;

            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scrollView.verticalScrollerVisibility = hasOverflow ? ScrollerVisibility.Auto : ScrollerVisibility.Hidden;
        }
    }
}
