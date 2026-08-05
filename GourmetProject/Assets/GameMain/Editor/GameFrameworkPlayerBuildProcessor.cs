using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityGameFramework.Editor.ResourceTools;
using GFPlatform = UnityGameFramework.Editor.ResourceTools.Platform;

namespace GourmetProject.Editor
{
    /// <summary>
    /// Builds GameFramework package resources before a Player build and stages only the
    /// target platform's runtime files at the StreamingAssets root expected by Package mode.
    /// Staged files are removed after the Player has been built.
    /// </summary>
    public sealed class GameFrameworkPlayerBuildProcessor :
        IPreprocessBuildWithReport,
        IPostprocessBuildWithReport
    {
        private const string ManifestDirectoryName = "GourmetProjectBuild";
        private const string ManifestFileName = "GameFrameworkStaging.txt";
        private const string ResourceCollectionPath =
            "Assets/GameFramework/Configs/ResourceCollection.xml";

        private static readonly ResourceDefinition[] ResourceDefinitions =
        {
            new ResourceDefinition(
                "materials/runtime",
                "t:Material",
                "Assets/GameMain/Content/Art/Materials",
                "Assets/GameMain/Content/Resources/Materials"),
            new ResourceDefinition(
                "prefabs/battle",
                "t:Prefab",
                "Assets/GameMain/Content/Prefabs/Battle"),
            new ResourceDefinition(
                "prefabs/ui",
                "t:Prefab",
                "Assets/GameMain/Content/Prefabs/UI"),
            new ResourceDefinition(
                "scenes/battle",
                "t:Scene",
                "Assets/GameMain/Content/Scenes/Battle.unity"),
            new ResourceDefinition(
                "shaders/runtime",
                "t:Shader",
                "Assets/GameMain/Content/Art/Shaders",
                "Assets/GameMain/Content/Resources/Shaders"),
            new ResourceDefinition(
                "sprites/backgrounds",
                "t:Texture2D",
                "Assets/GameMain/Content/Resources/Sprites/Backgrounds"),
            new ResourceDefinition(
                "sprites/battle",
                "t:Texture2D",
                "Assets/GameMain/Content/Resources/Sprites/Battle"),
            new ResourceDefinition(
                "sprites/characters",
                "t:Texture2D",
                "Assets/GameMain/Content/Resources/Sprites/Characters"),
            new ResourceDefinition(
                "sprites/dishes",
                "t:Texture2D",
                "Assets/GameMain/Content/Resources/Sprites/Dishes"),
            new ResourceDefinition(
                "sprites/items",
                "t:Texture2D",
                "Assets/GameMain/Content/Resources/Sprites/Items"),
            new ResourceDefinition(
                "sprites/ui",
                "t:Texture2D",
                "Assets/GameMain/Content/Resources/Sprites/UI"),
            new ResourceDefinition(
                "textures/effects",
                "t:Texture2D",
                "Assets/GameMain/Content/Resources/Particles",
                "Assets/GameMain/Content/Resources/Textures"),
        };

        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            CleanupStagedPackage();
            GenerateResourceCollection();

            GFPlatform platform = ToGameFrameworkPlatform(report.summary.platform);
            ResourceBuilderController controller = CreateController(platform);
            string packageDirectory = Path.Combine(controller.OutputPackagePath, platform.ToString());
            ValidatePackageFreshness(packageDirectory, platform);
            StagePackage(packageDirectory);
            Debug.Log($"[Build] GameFramework package staged from '{packageDirectory}'.");
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            CleanupStagedPackage();
            Debug.Log("[Build] Removed temporary GameFramework files from StreamingAssets.");
        }

        [MenuItem("Gourmet Project/Build/Build Window", false, 0)]
        private static void OpenBuildWindowMenu()
        {
            GameFrameworkBuildWindow.Open();
        }

        [MenuItem("Gourmet Project/Resources/Rebuild Active Target Package", false, 0)]
        private static void BuildActiveTargetPackageMenu()
        {
            GFPlatform platform =
                ToGameFrameworkPlatform(EditorUserBuildSettings.activeBuildTarget);
            BuildPackageResources(platform);
        }

        [MenuItem("Gourmet Project/Resources/Rebuild Resource Collection")]
        private static void GenerateResourceCollectionMenu()
        {
            int assetCount = GenerateResourceCollection();
            Debug.Log(
                $"[Build] Regenerated GameFramework resource collection with " +
                $"{ResourceDefinitions.Length} resources and {assetCount} assets.");
        }

        [MenuItem("Gourmet Project/Resources/Rebuild macOS Package")]
        private static void BuildMacOsPackageMenu()
        {
            BuildPackageResources(GFPlatform.MacOS);
        }

        [MenuItem("Gourmet Project/Build/Build Active Target Player", false, 1)]
        private static void BuildActiveTargetPlayerMenu()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            BuildPlayer(target, GetDefaultPlayerOutputPath(target));
        }

        [MenuItem("Gourmet Project/Build/Build macOS Player")]
        private static void BuildMacOsPlayerMenu()
        {
            BuildPlayer(
                BuildTarget.StandaloneOSX,
                GetDefaultPlayerOutputPath(BuildTarget.StandaloneOSX));
        }

        private static void BuildPlayer(BuildTarget target, string outputPath)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                throw new BuildFailedException(
                    $"Switch the active build target to '{target}' before building the Player.");
            }

            GFPlatform platform = ToGameFrameworkPlatform(target);
            BuildPackageResources(platform);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"{target} Player build failed with {report.summary.totalErrors} error(s).");
            }

            Debug.Log($"[Build] {target} Player created at '{outputPath}'.");
        }

        private static string GetDefaultPlayerOutputPath(BuildTarget target)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string playerRoot = Path.Combine(projectRoot, "Build", "Player");

            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return Path.Combine(playerRoot, "GourmetProject.exe");
                case BuildTarget.StandaloneOSX:
                    return Path.Combine(playerRoot, "GourmetProject.app");
                case BuildTarget.StandaloneLinux64:
                    return Path.Combine(playerRoot, "GourmetProject.x86_64");
                case BuildTarget.Android:
                    return Path.Combine(playerRoot, "GourmetProject.apk");
                case BuildTarget.iOS:
                    return Path.Combine(playerRoot, "iOS");
                case BuildTarget.WSAPlayer:
                    return Path.Combine(playerRoot, "WindowsStore");
                case BuildTarget.WebGL:
                    return Path.Combine(playerRoot, "WebGL");
                default:
                    throw new BuildFailedException(
                        $"No default Player output path is configured for '{target}'.");
            }
        }

        private static string GetPackageDirectory(GFPlatform platform)
        {
            ResourceBuilderController controller = CreateController(platform);
            return Path.Combine(controller.OutputPackagePath, platform.ToString());
        }

        private static ResourceBuilderController CreateController(GFPlatform platform)
        {
            var controller = new ResourceBuilderController();
            if (!controller.Load())
            {
                throw new BuildFailedException(
                    "Unable to load Assets/GameFramework/Configs/ResourceBuilder.xml.");
            }

            Directory.CreateDirectory(controller.OutputDirectory);

            foreach (GFPlatform candidate in Enum.GetValues(typeof(GFPlatform)))
            {
                if (candidate != GFPlatform.Undefined)
                {
                    controller.SelectPlatform(candidate, candidate == platform);
                }
            }

            controller.OutputPackageSelected = true;
            controller.OutputFullSelected = false;
            controller.OutputPackedSelected = false;

            if (!controller.RefreshCompressionHelper())
            {
                throw new BuildFailedException("Unable to initialize the GameFramework compression helper.");
            }

            if (!controller.RefreshBuildEventHandler())
            {
                throw new BuildFailedException("Unable to initialize the GameFramework build event handler.");
            }

            return controller;
        }

        private static string BuildPackageResources(GFPlatform platform)
        {
            GenerateResourceCollection();
            ResourceBuilderController controller = CreateController(platform);

            Debug.Log($"[Build] Building GameFramework package resources for {platform}...");
            if (!controller.BuildResources())
            {
                throw new BuildFailedException(
                    $"GameFramework resource build failed for {platform}. " +
                    $"See {controller.BuildReportPath} for details.");
            }

            string packageDirectory = Path.Combine(controller.OutputPackagePath, platform.ToString());
            Debug.Log($"[Build] GameFramework package created at '{packageDirectory}'.");
            return packageDirectory;
        }

        private static int GenerateResourceCollection()
        {
            var assets = new SortedDictionary<string, string>(StringComparer.Ordinal);

            foreach (ResourceDefinition definition in ResourceDefinitions)
            {
                foreach (string guid in FindAssetGuids(definition))
                {
                    if (assets.TryGetValue(guid, out string existingResource))
                    {
                        throw new BuildFailedException(
                            $"Asset '{AssetDatabase.GUIDToAssetPath(guid)}' is assigned to both " +
                            $"'{existingResource}' and '{definition.Name}'.");
                    }

                    assets.Add(guid, definition.Name);
                }
            }

            foreach (ResourceDefinition definition in ResourceDefinitions)
            {
                if (!assets.ContainsValue(definition.Name))
                {
                    throw new BuildFailedException(
                        $"GameFramework resource '{definition.Name}' has no assets.");
                }
            }

            string absolutePath = Path.GetFullPath(
                Path.Combine(Directory.GetParent(Application.dataPath).FullName, ResourceCollectionPath));
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));

            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(true),
                Indent = true,
                IndentChars = "  ",
                NewLineChars = Environment.NewLine,
                NewLineHandling = NewLineHandling.Replace,
            };

            byte[] contents;
            using (var stream = new MemoryStream())
            {
                using (XmlWriter writer = XmlWriter.Create(stream, settings))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("UnityGameFramework");
                    writer.WriteStartElement("ResourceCollection");

                    writer.WriteStartElement("Resources");
                    foreach (ResourceDefinition definition in ResourceDefinitions)
                    {
                        writer.WriteStartElement("Resource");
                        writer.WriteAttributeString("Name", definition.Name);
                        writer.WriteAttributeString("LoadType", "0");
                        writer.WriteAttributeString("Packed", "True");
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteStartElement("Assets");

                    foreach (KeyValuePair<string, string> asset in assets)
                    {
                        writer.WriteStartElement("Asset");
                        writer.WriteAttributeString("Guid", asset.Key);
                        writer.WriteAttributeString("ResourceName", asset.Value);
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }

                contents = stream.ToArray();
            }

            bool changed = !File.Exists(absolutePath) ||
                           !File.ReadAllBytes(absolutePath).SequenceEqual(contents);

            if (changed)
            {
                File.WriteAllBytes(absolutePath, contents);
                AssetDatabase.ImportAsset(
                    ResourceCollectionPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }

            return assets.Count;
        }

        private static IEnumerable<string> FindAssetGuids(ResourceDefinition definition)
        {
            foreach (string searchPath in definition.SearchPaths)
            {
                if (AssetDatabase.IsValidFolder(searchPath))
                {
                    foreach (string guid in AssetDatabase.FindAssets(
                                 definition.Filter,
                                 new[] { searchPath }))
                    {
                        yield return guid;
                    }

                    continue;
                }

                string guidForPath = AssetDatabase.AssetPathToGUID(searchPath);
                if (string.IsNullOrEmpty(guidForPath))
                {
                    throw new BuildFailedException(
                        $"Resource collection source does not exist: {searchPath}");
                }

                yield return guidForPath;
            }
        }

        private static void StagePackage(string sourceRoot)
        {
            sourceRoot = Path.GetFullPath(sourceRoot);
            if (!Directory.Exists(sourceRoot))
            {
                throw new BuildFailedException(
                    $"GameFramework package directory does not exist: {sourceRoot}");
            }

            string versionFile = Path.Combine(sourceRoot, "GameFrameworkVersion.dat");
            if (!File.Exists(versionFile))
            {
                throw new BuildFailedException(
                    $"GameFramework package is incomplete; missing '{versionFile}'.");
            }

            string streamingAssetsRoot = Path.GetFullPath(Application.streamingAssetsPath);
            Directory.CreateDirectory(streamingAssetsRoot);

            var stagedFiles = new List<string>();
            var createdDirectories = new HashSet<string>(StringComparer.Ordinal);

            foreach (string sourceFile in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                if (sourceFile.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
                string destinationFile = Path.GetFullPath(Path.Combine(streamingAssetsRoot, relativePath));
                string destinationDirectory = Path.GetDirectoryName(destinationFile);

                EnsureDirectory(destinationDirectory, streamingAssetsRoot, createdDirectories);
                File.Copy(sourceFile, destinationFile, true);
                stagedFiles.Add(destinationFile);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            foreach (string stagedFile in stagedFiles.ToArray())
            {
                string metaFile = stagedFile + ".meta";
                if (File.Exists(metaFile))
                {
                    stagedFiles.Add(metaFile);
                }
            }

            foreach (string directory in createdDirectories)
            {
                string metaFile = directory + ".meta";
                if (File.Exists(metaFile))
                {
                    stagedFiles.Add(metaFile);
                }
            }

            string manifestPath = GetManifestPath();
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath));
            IEnumerable<string> manifestLines =
                stagedFiles.Select(path => $"F|{path}")
                    .Concat(createdDirectories.Select(path => $"D|{path}"));
            File.WriteAllLines(manifestPath, manifestLines);
        }

        private static void ValidatePackageFreshness(
            string packageDirectory,
            GFPlatform platform)
        {
            string versionFile = Path.Combine(packageDirectory, "GameFrameworkVersion.dat");
            if (!File.Exists(versionFile))
            {
                throw new BuildFailedException(
                    $"GameFramework package resources for {platform} are missing at " +
                    $"'{packageDirectory}'. Open 'Gourmet Project/Build/Build Window' and run " +
                    $"'Rebuild {platform} Package' before building.");
            }

            DateTime packageTime = File.GetLastWriteTimeUtc(versionFile);
            var sourcePaths = new List<string>
            {
                ResourceCollectionPath,
                "Assets/GameFramework/Configs/ResourceBuilder.xml",
            };

            foreach (ResourceDefinition definition in ResourceDefinitions)
            {
                sourcePaths.AddRange(
                    FindAssetGuids(definition).Select(AssetDatabase.GUIDToAssetPath));
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string stalePath = sourcePaths
                .Distinct(StringComparer.Ordinal)
                .FirstOrDefault(path =>
                {
                    string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, path));
                    return File.Exists(absolutePath) &&
                           File.GetLastWriteTimeUtc(absolutePath) > packageTime;
                });

            if (!string.IsNullOrEmpty(stalePath))
            {
                throw new BuildFailedException(
                    $"GameFramework package resources for {platform} are older than " +
                    $"'{stalePath}'. Open 'Gourmet Project/Build/Build Window' and run " +
                    $"'Rebuild {platform} Package' before building.");
            }
        }

        private static void EnsureDirectory(
            string directory,
            string root,
            ISet<string> createdDirectories)
        {
            if (Directory.Exists(directory))
            {
                return;
            }

            string parent = Path.GetDirectoryName(directory);
            if (!string.IsNullOrEmpty(parent) &&
                !string.Equals(directory, root, StringComparison.Ordinal))
            {
                EnsureDirectory(parent, root, createdDirectories);
            }

            Directory.CreateDirectory(directory);
            createdDirectories.Add(directory);
        }

        private static void CleanupStagedPackage()
        {
            string manifestPath = GetManifestPath();
            if (!File.Exists(manifestPath))
            {
                return;
            }

            string streamingAssetsRoot = Path.GetFullPath(Application.streamingAssetsPath);
            string rootPrefix = streamingAssetsRoot + Path.DirectorySeparatorChar;
            string[] lines = File.ReadAllLines(manifestPath);

            foreach (string line in lines.Where(line => line.StartsWith("F|", StringComparison.Ordinal)))
            {
                string path = Path.GetFullPath(line.Substring(2));
                if (path.StartsWith(rootPrefix, StringComparison.Ordinal) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            foreach (string line in lines
                         .Where(line => line.StartsWith("D|", StringComparison.Ordinal))
                         .OrderByDescending(line => line.Length))
            {
                string path = Path.GetFullPath(line.Substring(2));
                if (path.StartsWith(rootPrefix, StringComparison.Ordinal) &&
                    Directory.Exists(path) &&
                    !Directory.EnumerateFileSystemEntries(path).Any())
                {
                    Directory.Delete(path);
                }
            }

            File.Delete(manifestPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static string GetManifestPath()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(
                projectRoot,
                "Library",
                ManifestDirectoryName,
                ManifestFileName);
        }

        private static GFPlatform ToGameFrameworkPlatform(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                    return GFPlatform.Windows;
                case BuildTarget.StandaloneWindows64:
                    return GFPlatform.Windows64;
                case BuildTarget.StandaloneOSX:
                    return GFPlatform.MacOS;
                case BuildTarget.StandaloneLinux64:
                    return GFPlatform.Linux;
                case BuildTarget.iOS:
                    return GFPlatform.IOS;
                case BuildTarget.Android:
                    return GFPlatform.Android;
                case BuildTarget.WSAPlayer:
                    return GFPlatform.WindowsStore;
                case BuildTarget.WebGL:
                    return GFPlatform.WebGL;
                default:
                    throw new BuildFailedException(
                        $"GameFramework package staging does not support build target '{target}'.");
            }
        }

        private sealed class ResourceDefinition
        {
            public ResourceDefinition(string name, string filter, params string[] searchPaths)
            {
                Name = name;
                Filter = filter;
                SearchPaths = searchPaths;
            }

            public string Name { get; }
            public string Filter { get; }
            public string[] SearchPaths { get; }
        }

        private sealed class GameFrameworkBuildWindow : EditorWindow
        {
            private const float LabelWidth = 130f;

            private Vector2 _scrollPosition;
            private string _packageDirectory;
            private string _packageStatus;
            private MessageType _packageStatusType;
            private string _lastOperation;
            private MessageType _lastOperationType;

            public static void Open()
            {
                GameFrameworkBuildWindow window =
                    GetWindow<GameFrameworkBuildWindow>("Gourmet Build");
                window.minSize = new Vector2(500f, 430f);
                window.RefreshStatus();
                window.Show();
            }

            private void OnEnable()
            {
                titleContent = new GUIContent("Gourmet Build");
                RefreshStatus();
            }

            private void OnFocus()
            {
                RefreshStatus();
                Repaint();
            }

            private void OnGUI()
            {
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

                GUILayout.Space(8f);
                EditorGUILayout.LabelField("Gourmet Project Build", EditorStyles.boldLabel);
                EditorGUILayout.Space(4f);

                DrawBuildOverview();
                EditorGUILayout.Space(8f);
                DrawResourceActions();
                EditorGUILayout.Space(8f);
                DrawPlayerActions();

                if (!string.IsNullOrEmpty(_lastOperation))
                {
                    EditorGUILayout.Space(8f);
                    EditorGUILayout.HelpBox(_lastOperation, _lastOperationType);
                }

                EditorGUILayout.EndScrollView();
            }

            private void DrawBuildOverview()
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Build Overview", EditorStyles.boldLabel);

                    float previousLabelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = LabelWidth;
                    EditorGUILayout.LabelField(
                        "Active target",
                        EditorUserBuildSettings.activeBuildTarget.ToString());
                    EditorGUILayout.LabelField(
                        "Enabled scenes",
                        EditorBuildSettings.scenes.Count(scene => scene.enabled).ToString());
                    EditorGUILayout.LabelField(
                        "Player output",
                        GetActivePlayerOutputPathOrUnavailable());
                    EditorGUILayout.LabelField(
                        "Package output",
                        string.IsNullOrEmpty(_packageDirectory) ? "Unavailable" : _packageDirectory);
                    EditorGUIUtility.labelWidth = previousLabelWidth;

                    EditorGUILayout.Space(4f);
                    EditorGUILayout.HelpBox(
                        string.IsNullOrEmpty(_packageStatus)
                            ? "Package status has not been checked."
                            : _packageStatus,
                        _packageStatusType);

                    if (GUILayout.Button("Refresh Status"))
                    {
                        RefreshStatus();
                    }
                }
            }

            private void DrawResourceActions()
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("GameFramework Resources", EditorStyles.boldLabel);

                    bool editorBusy = IsEditorBusy();
                    bool isSupported = TryGetActivePlatform(out GFPlatform platform);
                    using (new EditorGUI.DisabledScope(editorBusy || !isSupported))
                    {
                        if (GUILayout.Button("Rebuild Resource Collection", GUILayout.Height(28f)))
                        {
                            RunOperation(
                                "Resource collection rebuild",
                                GenerateResourceCollectionMenu);
                        }

                        string packageButtonLabel = isSupported
                            ? $"Rebuild {platform} Package"
                            : "Rebuild Active Target Package";
                        if (GUILayout.Button(packageButtonLabel, GUILayout.Height(32f)))
                        {
                            RunOperation(
                                $"{platform} package rebuild",
                                () => BuildPackageResources(platform));
                        }
                    }

                    using (new EditorGUI.DisabledScope(
                               string.IsNullOrEmpty(_packageDirectory) ||
                               !Directory.Exists(_packageDirectory)))
                    {
                        if (GUILayout.Button("Reveal Package Output"))
                        {
                            EditorUtility.RevealInFinder(_packageDirectory);
                        }
                    }

                    if (editorBusy)
                    {
                        EditorGUILayout.HelpBox(
                            "Wait for the current compilation, import, or Player build to finish.",
                            MessageType.Info);
                    }
                    else if (!isSupported)
                    {
                        EditorGUILayout.HelpBox(
                            $"GameFramework package building does not support " +
                            $"'{EditorUserBuildSettings.activeBuildTarget}'.",
                            MessageType.Error);
                    }
                }
            }

            private void DrawPlayerActions()
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Player", EditorStyles.boldLabel);

                    BuildTarget activeTarget = EditorUserBuildSettings.activeBuildTarget;
                    bool isSupported = TryGetActivePlatform(out GFPlatform platform);
                    bool editorBusy = IsEditorBusy();

                    if (!isSupported)
                    {
                        EditorGUILayout.HelpBox(
                            $"The active build target '{activeTarget}' is not supported.",
                            MessageType.Error);
                    }

                    using (new EditorGUI.DisabledScope(editorBusy || !isSupported))
                    {
                        string buildButtonLabel = isSupported
                            ? $"Build {platform} Player"
                            : "Build Active Target Player";
                        if (GUILayout.Button(buildButtonLabel, GUILayout.Height(38f)))
                        {
                            RunOperation(
                                $"{platform} Player build",
                                () => BuildPlayer(
                                    activeTarget,
                                    GetDefaultPlayerOutputPath(activeTarget)));
                        }
                    }

                    string playerOutputPath = GetActivePlayerOutputPathOrUnavailable();
                    string playerDirectory =
                        playerOutputPath == "Unavailable"
                            ? null
                            : Path.GetDirectoryName(playerOutputPath);
                    using (new EditorGUI.DisabledScope(
                               string.IsNullOrEmpty(playerDirectory) ||
                               !Directory.Exists(playerDirectory)))
                    {
                        if (GUILayout.Button("Reveal Player Output"))
                        {
                            EditorUtility.RevealInFinder(playerDirectory);
                        }
                    }

                    EditorGUILayout.Space(6f);
                    EditorGUILayout.LabelField("Switch Active Target", EditorStyles.miniBoldLabel);
                    using (new EditorGUI.DisabledScope(editorBusy))
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(
                                   activeTarget == BuildTarget.StandaloneWindows64))
                        {
                            if (GUILayout.Button("Windows 64-bit"))
                            {
                                RunOperation(
                                    "Windows 64-bit target switch",
                                    () => SwitchActiveTarget(BuildTarget.StandaloneWindows64));
                            }
                        }

                        using (new EditorGUI.DisabledScope(
                                   activeTarget == BuildTarget.StandaloneOSX))
                        {
                            if (GUILayout.Button("macOS"))
                            {
                                RunOperation(
                                    "macOS target switch",
                                    () => SwitchActiveTarget(BuildTarget.StandaloneOSX));
                            }
                        }
                    }
                }
            }

            private void RefreshStatus()
            {
                if (EditorApplication.isCompiling)
                {
                    _packageStatus = "Waiting for script compilation to finish.";
                    _packageStatusType = MessageType.Info;
                    return;
                }

                try
                {
                    if (!TryGetActivePlatform(out GFPlatform platform))
                    {
                        _packageDirectory = null;
                        _packageStatus =
                            $"The active build target " +
                            $"'{EditorUserBuildSettings.activeBuildTarget}' is not supported.";
                        _packageStatusType = MessageType.Error;
                        return;
                    }

                    _packageDirectory = GetPackageDirectory(platform);
                    ValidatePackageFreshness(_packageDirectory, platform);
                    _packageStatus =
                        $"{platform} package is present and up to date.";
                    _packageStatusType = MessageType.Info;
                }
                catch (BuildFailedException exception)
                {
                    _packageStatus = exception.Message;
                    _packageStatusType = MessageType.Warning;
                }
                catch (Exception exception)
                {
                    _packageStatus = $"Unable to check package status: {exception.Message}";
                    _packageStatusType = MessageType.Error;
                }
            }

            private void RunOperation(string operationName, Action operation)
            {
                try
                {
                    operation();
                    _lastOperation = $"{operationName} completed successfully.";
                    _lastOperationType = MessageType.Info;
                }
                catch (Exception exception)
                {
                    _lastOperation = $"{operationName} failed: {exception.Message}";
                    _lastOperationType = MessageType.Error;
                    Debug.LogException(exception);
                }
                finally
                {
                    RefreshStatus();
                    Repaint();
                }
            }

            private static void SwitchActiveTarget(BuildTarget target)
            {
                BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(target);
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                        targetGroup,
                        target))
                {
                    throw new BuildFailedException(
                        $"Unity could not switch the active build target to '{target}'.");
                }
            }

            private static bool TryGetActivePlatform(out GFPlatform platform)
            {
                try
                {
                    platform =
                        ToGameFrameworkPlatform(EditorUserBuildSettings.activeBuildTarget);
                    return true;
                }
                catch (BuildFailedException)
                {
                    platform = GFPlatform.Undefined;
                    return false;
                }
            }

            private static string GetActivePlayerOutputPathOrUnavailable()
            {
                try
                {
                    return GetDefaultPlayerOutputPath(
                        EditorUserBuildSettings.activeBuildTarget);
                }
                catch (BuildFailedException)
                {
                    return "Unavailable";
                }
            }

            private static bool IsEditorBusy()
            {
                return EditorApplication.isCompiling ||
                       EditorApplication.isUpdating ||
                       BuildPipeline.isBuildingPlayer;
            }
        }
    }
}
