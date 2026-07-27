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
                "sprites/ui-layout",
                "t:Texture2D",
                "Assets/GameMain/Development/ArtPreviews/BattleFormUI/split_simple_v01/pieces/left_button_bottom.png",
                "Assets/GameMain/Development/ArtPreviews/BattleFormUI/split_simple_v01/pieces/left_button_top.png",
                "Assets/GameMain/Development/ArtPreviews/BattleFormUI/split_simple_v01/pieces/left_coin_card.png",
                "Assets/GameMain/Development/ArtPreviews/BattleFormUI/split_simple_v01/pieces/left_score_card.png",
                "Assets/GameMain/Development/ArtPreviews/BattleFormUI/split_simple_v01/pieces/left_week_card.png"),
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
            ValidatePackageFreshness(packageDirectory);
            StagePackage(packageDirectory);
            Debug.Log($"[Build] GameFramework package staged from '{packageDirectory}'.");
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            CleanupStagedPackage();
            Debug.Log("[Build] Removed temporary GameFramework files from StreamingAssets.");
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

        [MenuItem("Gourmet Project/Build/Build macOS Player")]
        private static void BuildMacOsPlayerMenu()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneOSX)
            {
                throw new BuildFailedException(
                    "Switch the active build target to macOS before building the Player.");
            }

            BuildPackageResources(GFPlatform.MacOS);

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputPath = Path.Combine(projectRoot, "Build", "Player", "GourmetProject.app");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"macOS Player build failed with {report.summary.totalErrors} error(s).");
            }

            Debug.Log($"[Build] macOS Player created at '{outputPath}'.");
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

        private static void ValidatePackageFreshness(string packageDirectory)
        {
            string versionFile = Path.Combine(packageDirectory, "GameFrameworkVersion.dat");
            if (!File.Exists(versionFile))
            {
                throw new BuildFailedException(
                    "GameFramework package resources are missing. Run " +
                    "'Gourmet Project/Resources/Rebuild macOS Package' before building.");
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
                    $"GameFramework package resources are older than '{stalePath}'. Run " +
                    "'Gourmet Project/Resources/Rebuild macOS Package' before building.");
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
    }
}
