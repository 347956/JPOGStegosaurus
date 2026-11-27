using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JPOGBuild
{
    /// <summary>
    /// Builds only this mod's Dusk bundles into the AssetBundles/ folder of this repo.
    /// The Asset Bundle Browser has one output folder for the whole project and builds every bundle,
    /// so it would mix this mod's bundles with other mods in the same Unity project.
    /// Batch mode: -executeMethod JPOGBuild.JPOGStegosaurusBundleBuilder.Build
    /// </summary>
    public static class JPOGStegosaurusBundleBuilder
    {
        const string ModInformation = "JPOGStegosaurusModInformation";
        static readonly string[] Bundles = { "jpogstegosaurusmainassets.duskmod", "jpogstegosaurusassets" };

        [MenuItem("JPOG/Build JPOGStegosaurus bundles")]
        public static void Build()
        {
            // Find this repo's folder through its mod information, so it works wherever the folder is placed.
            string infoPath = AssetDatabase.FindAssets(ModInformation)
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == ModInformation);
            if (infoPath == null) throw new Exception("[JPOGStegosaurus] " + ModInformation + ".asset not found");
            string root = Path.GetDirectoryName(infoPath).Replace('\\', '/');
            string outDir = Path.GetFullPath(Path.Combine(root, "AssetBundles"));
            Directory.CreateDirectory(outDir);

            // Reimport the content first: a prefab imported while the plugin DLL was missing keeps an import
            // without its script data, and the bundle would ship the AI with every field at its default.
            AssetDatabase.ImportAsset(root + "/Content", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
            ValidateEnemyPrefabs(root + "/Content");

            var builds = Bundles.Select(bundle =>
            {
                string[] assets = AssetDatabase.GetAssetPathsFromAssetBundle(bundle)
                    .Where(p => !p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                             && !p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (assets.Length == 0) throw new Exception("[JPOGStegosaurus] No assets assigned to bundle " + bundle);
                return new AssetBundleBuild { assetBundleName = bundle, assetNames = assets };
            }).ToArray();

            // Both bundles in one build so Unity resolves the references between them.
            // LZ4 (chunk based) like LCDrone: faster to load than LZMA, a bit bigger.
            var manifest = BuildPipeline.BuildAssetBundles(outDir, builds,
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new Exception("[JPOGStegosaurus] Bundle build failed");

            foreach (string bundle in Bundles)
                Debug.Log("[JPOGStegosaurus] Built " + bundle + ": " + new FileInfo(Path.Combine(outDir, bundle)).Length + " bytes");
            Debug.Log("[JPOGStegosaurus] Bundles written to " + outDir + ". Run dotnet build in Plugin~ to deploy them.");
        }
            /// <summary>
        /// Fails the build when an enemy prefab's AI component (the MonoBehaviour with an enemyType field)
        /// has no script or no enemyType. LethalLevelLoader rejects such an enemy and its error breaks
        /// StartOfRound.Awake, so a new save throws errors everywhere.
        /// </summary>
        static void ValidateEnemyPrefabs(string contentFolder)
        {
            int checkedCount = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { contentFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null)
                        throw new Exception("[JPOGStegosaurus] " + path + " has a missing script. Build the plugin DLL first.");
                    var enemyType = new SerializedObject(behaviour).FindProperty("enemyType");
                    if (enemyType == null || enemyType.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (behaviour.GetType().Name == "EnemyAICollisionDetect") continue;
                    if (enemyType.objectReferenceValue == null)
                        throw new Exception("[JPOGStegosaurus] " + path + ": " + behaviour.GetType().Name + ".enemyType is empty");
                    checkedCount++;
                }
            }
            if (checkedCount == 0) throw new Exception("[JPOGStegosaurus] No enemy AI component found under " + contentFolder);
            Debug.Log("[JPOGStegosaurus] Enemy prefab check passed (" + checkedCount + " AI component(s) with enemyType set)");
        }
    }
}
