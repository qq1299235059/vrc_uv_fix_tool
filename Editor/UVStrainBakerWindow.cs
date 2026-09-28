using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UVStrainBaker
{
    public sealed class UVStrainBakerWindow : EditorWindow
    {
        private SkinnedMeshRenderer target;
        private int sourceChannel;
        private int outputChannel = 1;
        private float strength = 1f;
        private bool weldCoincident = true;
        private float maxPrincipalStretch = 8f;
        private bool assignToRenderer = true;
        private string assetPath = "Assets/UVStrainBaker/Generated";
        private string reportText;
        private Vector2 scroll;

        [MenuItem("Tools/UV Strain Baker")]
        private static void Open() => GetWindow<UVStrainBakerWindow>("Static Strain Bake");

        [MenuItem("Tools/UV Tools/2 Strain Bake")]
        private static void OpenModule() => Open();

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Bake a static correction into a new Mesh Asset. BakeMesh is used only as the current-shape sample; the output is cloned from sharedMesh.", MessageType.Info);
            target = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Target Renderer", target, typeof(SkinnedMeshRenderer), true);
            using (new EditorGUILayout.HorizontalScope())
            {
                sourceChannel = EditorGUILayout.IntSlider("Source Channel", sourceChannel, 0, 7);
                GUILayout.Label(ChannelLabel(sourceChannel), GUILayout.Width(120));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                outputChannel = EditorGUILayout.IntSlider("Output Channel", outputChannel, 0, 7);
                GUILayout.Label(ChannelLabel(outputChannel), GUILayout.Width(120));
            }
            strength = EditorGUILayout.Slider("Correction Strength", strength, 0, 1);
            weldCoincident = EditorGUILayout.Toggle("Weld Coincident UV Vertices", weldCoincident);
            maxPrincipalStretch = EditorGUILayout.FloatField("Max Principal Stretch", maxPrincipalStretch);
            assignToRenderer = EditorGUILayout.Toggle("Assign To Renderer", assignToRenderer);
            assetPath = EditorGUILayout.TextField("Asset Folder", assetPath);
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Analyze Current State", GUILayout.Height(28))) Run(false);
                if (GUILayout.Button("Bake Corrected UV", GUILayout.Height(28))) Run(true);
            }
            if (!string.IsNullOrEmpty(reportText))
            {
                EditorGUILayout.Space(4);
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(150));
                EditorGUILayout.SelectableLabel(reportText, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndScrollView();
            }
        }

        private void Run(bool bake)
        {
            try
            {
                Mesh source = ValidateTarget();
                SolveInput input = MeshSnapshotReader.Read(target, source, sourceChannel,
                    strength, weldCoincident, maxPrincipalStretch);
                SolveResult result = UvStrainSolver.Solve(input);
                reportText = result.Report.Summary;
                Debug.Log("UV Strain Baker\n" + reportText, target);
                if (bake)
                {
                    if (result.Report.FlipCount > 0)
                        throw new InvalidOperationException("Output UV contains orientation flips; Bake is blocked so the original renderer is not replaced.");
                    MeshAssetWriter.Write(target, source, result.CorrectedUv,
                        outputChannel, assetPath, assignToRenderer);
                }
                Repaint();
            }
            catch (Exception ex)
            {
                reportText = ex.Message;
                Debug.LogError("UV Strain Baker: " + ex, target);
            }
        }

        private Mesh ValidateTarget()
        {
            if (target == null) throw new InvalidOperationException("Assign a SkinnedMeshRenderer first.");
            if (target.sharedMesh == null) throw new InvalidOperationException("Target renderer has no shared Mesh.");
            Vector3 scale = target.transform.localScale;
            if ((scale - Vector3.one).sqrMagnitude > 1e-8f)
                throw new InvalidOperationException("Transform Scale must be (1,1,1) for MVP baking.");
            if (sourceChannel == outputChannel)
                Debug.LogWarning("Source and output channels are identical; source UVs will be overwritten.", target);
            var outputAttribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + outputChannel);
            Mesh source = target.sharedMesh;
            if (source.HasVertexAttribute(outputAttribute) && source.GetVertexAttributeDimension(outputAttribute) > 2)
                throw new InvalidOperationException($"Output channel {outputChannel} is Vector3/Vector4; MVP refuses to discard Z/W data.");
            if (maxPrincipalStretch < 0.125f) throw new InvalidOperationException("Max Principal Stretch must be at least 0.125.");
            return target.sharedMesh;
        }

        private static string ChannelLabel(int channel) => $"UV{channel} / TEXCOORD{channel}";
    }

    internal static class MeshSnapshotReader
    {
        public static SolveInput Read(SkinnedMeshRenderer renderer, Mesh source, int sourceChannel,
            float strength, bool weld, float maxStretch)
        {
            Vector3[] referencePositions;
            Vector2[] sourceUv;
            var triangles = new List<int>();
            var uvAttribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + sourceChannel);
            if (!source.HasVertexAttribute(uvAttribute))
                throw new InvalidOperationException($"Source channel {sourceChannel} is not present on the Mesh.");
            // MeshUtility is the Editor-only read path that can inspect imported meshes even
            // when Read/Write Enabled is off on the model importer.
            using (var meshDataArray = MeshUtility.AcquireReadOnlyMeshData(source))
            {
                var data = meshDataArray[0];
                using (var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                using (var uvs = new NativeArray<Vector2>(data.vertexCount, Allocator.Temp))
                {
                    data.GetVertices(vertices);
                    data.GetUVs(sourceChannel, uvs);
                    referencePositions = vertices.ToArray();
                    sourceUv = uvs.ToArray();
                }
                for (int submesh = 0; submesh < data.subMeshCount; submesh++)
                {
                    var descriptor = data.GetSubMesh(submesh);
                    if (descriptor.topology != MeshTopology.Triangles)
                        throw new InvalidOperationException($"Submesh {submesh} is not triangles; MVP cannot solve it.");
                    using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                    {
                        data.GetIndices(indices, submesh, true);
                        triangles.AddRange(indices.ToArray());
                    }
                }
            }
            if (sourceUv.Length != source.vertexCount)
                throw new InvalidOperationException($"Source channel {sourceChannel} has {sourceUv.Length} UVs for {source.vertexCount} vertices.");
            var snapshot = new Mesh { name = source.name + "__UVStrainSnapshot" };
            try
            {
                renderer.BakeMesh(snapshot);
                if (snapshot.vertexCount != source.vertexCount)
                    throw new InvalidOperationException("BakeMesh vertex count differs from sharedMesh; refusing to guess correspondence.");
                return new SolveInput
                {
                    ReferencePositions = referencePositions,
                    CurrentPositions = snapshot.vertices,
                    SourceUv = sourceUv,
                    Triangles = triangles.ToArray(),
                    Strength = strength,
                    WeldCoincident = weld,
                    MaximumStretch = maxStretch
                };
            }
            finally { UnityEngine.Object.DestroyImmediate(snapshot); }
        }
    }

    internal static class MeshAssetWriter
    {
        public static Mesh Write(SkinnedMeshRenderer renderer, Mesh source, Vector2[] outputUv,
            int outputChannel, string folder, bool assign)
        {
            if (outputUv.Length != source.vertexCount) throw new ArgumentException("Output UV count mismatch.");
            folder = folder.Replace('\\', '/').TrimEnd('/');
            EnsureFolder(folder);
            string baseName = source.name + "_UVStrain.asset";
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + baseName);
            Mesh output = UnityEngine.Object.Instantiate(source);
            output.name = Path.GetFileNameWithoutExtension(path);
            output.SetUVs(outputChannel, new List<Vector2>(outputUv));
            AssetDatabase.CreateAsset(output, path);
            AssetDatabase.SaveAssets();
            if (assign)
            {
                Undo.RecordObject(renderer, "Assign UV Strain Mesh");
                renderer.sharedMesh = output;
                EditorUtility.SetDirty(renderer);
            }
            Selection.activeObject = output;
            Debug.Log("Wrote UV Strain Mesh: " + path, output);
            return output;
        }

        private static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}

