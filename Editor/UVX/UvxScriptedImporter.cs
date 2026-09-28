using System;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace UVStrainBaker
{
    [ScriptedImporter(1, "uvx")]
    public sealed class UvxScriptedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var asset = ScriptableObject.CreateInstance<UvxAsset>();
            try
            {
                UvxData data = UvxBinaryReader.Read(context.assetPath);
                asset.SetMetadata(1, 0, data.TargetName, data.AnchorName,
                    data.Positions.Length, data.LoopVertex.Length, data.Polygons.Length,
                    data.TriangleCount, "Compatible");
            }
            catch (Exception exception)
            {
                asset.SetMetadata(0, 0, string.Empty, string.Empty, 0, 0, 0, 0,
                    "Corrupt: " + exception.Message);
                Debug.LogError("UVX import failed for " + context.assetPath + ": " + exception.Message);
            }
            asset.name = System.IO.Path.GetFileNameWithoutExtension(context.assetPath) + "_UVX";
            context.AddObjectToAsset("UVX", asset);
            context.SetMainObject(asset);
        }
    }

    [CustomEditor(typeof(UvxAsset))]
    internal sealed class UvxAssetEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (UvxAsset)target;
            EditorGUILayout.LabelField("Status", asset.ValidationState);
            EditorGUILayout.LabelField("Target UV", asset.TargetUvName);
            EditorGUILayout.LabelField("Anchor UV", asset.AnchorUvName);
            EditorGUILayout.LabelField("Vertices / Loops", $"{asset.VertexCount} / {asset.LoopCount}");
            EditorGUILayout.LabelField("Polygons / Triangles", $"{asset.PolygonCount} / {asset.TriangleCount}");
        }
    }
}

