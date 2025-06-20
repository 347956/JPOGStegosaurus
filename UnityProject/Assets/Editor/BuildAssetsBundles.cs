using System.IO;
using System;
using UnityEditor;
using UnityEngine;


public class BuildAssetsBundles : MonoBehaviour
{
    [MenuItem("Tools/Build AssetBundles")]
    public static void BuildAllAssetBundles()
    {
        string outputPath = Path.Combine("../UnityProject/AssetBundles/StandaloneWindows/");

        try
        {
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            BuildPipeline.BuildAssetBundles(
                outputPath,
                BuildAssetBundleOptions.None,
                EditorUserBuildSettings.activeBuildTarget
            );
        }
        catch(Exception e)
        {
            Debug.Log(e);
        }
        Debug.Log("AssetBundles built to: " + Path.GetFullPath(outputPath));
    }
}
