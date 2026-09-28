using System;
using UnityEditor;
using UnityEngine;

namespace UVStrainBaker
{
    public sealed class UVXImportWindow : EditorWindow
    {
        private UvxAsset uvxAsset;
        private SkinnedMeshRenderer target;
        private int anchorChannel;
        private int outputChannel = 1;
        private bool assignToRenderer = true;
        private string assetPath = "Assets/UVStrainBaker/Generated";
        private string reportText;
        private Vector2 scroll;

        [MenuItem("Tools/UV Tools/1 Import Blender UVX")]
        private static void Open() => GetWindow<UVXImportWindow>("Import Blender UVX");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Module 1: import a Blender Target UV map through its Anchor UV. This module does not sample deformation or run strain correction.", MessageType.Info);
            uvxAsset = (UvxAsset)EditorGUILayout.ObjectField("UVX Asset", uvxAsset, typeof(UvxAsset), false);
            target = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Target Renderer", target, typeof(SkinnedMeshRenderer), true);
            anchorChannel = EditorGUILayout.IntSlider("Unity Anchor Channel", anchorChannel, 0, 7);
            outputChannel = EditorGUILayout.IntSlider("Output Channel", outputChannel, 0, 7);
            assignToRenderer = EditorGUILayout.Toggle("Assign To Renderer", assignToRenderer);
            assetPath = EditorGUILayout.TextField("Asset Folder", assetPath);
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Analyze Mapping", GUILayout.Height(28))) Run(false);
                if (GUILayout.Button("Apply To New Mesh", GUILayout.Height(28))) Run(true);
            }
            if (!string.IsNullOrEmpty(reportText))
            {
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(140));
                EditorGUILayout.SelectableLabel(reportText, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndScrollView();
            }
        }

        private void Run(bool apply)
        {
            try
            {
                if (uvxAsset == null) throw new InvalidOperationException("Assign a UVX asset first.");
                if (target == null || target.sharedMesh == null) throw new InvalidOperationException("Assign a target SkinnedMeshRenderer with a Mesh.");
                if (uvxAsset.ValidationState != "Compatible") throw new InvalidOperationException(uvxAsset.ValidationState);
                string path = AssetDatabase.GetAssetPath(uvxAsset);
                UvxData data = UvxBinaryReader.Read(path);
                UvxMapping mapping = UvxAnchorMapper.Map(data, target.sharedMesh, anchorChannel);
                reportText = mapping.Summary;
                Debug.Log("UVX Mapping\n" + reportText, target);
                if (apply)
                {
                    UvxAssetWriter.Write(target, target.sharedMesh, mapping.OutputUv,
                        outputChannel, assetPath, assignToRenderer);
                    reportText += "\nCreated a new Mesh Asset without changing vertex count.";
                }
            }
            catch (Exception exception)
            {
                reportText = exception.Message;
                Debug.LogError("UVX import: " + exception, target);
            }
            Repaint();
        }
    }
}

