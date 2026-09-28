using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UVStrainBaker
{
    internal static class UvxAssetWriter
    {
        public static Mesh Write(SkinnedMeshRenderer renderer, Mesh source, Vector2[] targetUv,
            int outputChannel, string folder, bool assign)
        {
            if (targetUv == null || targetUv.Length != source.vertexCount)
                throw new InvalidDataException("Mapped UV count does not match the Unity Mesh.");
            if (outputChannel < 0 || outputChannel > 7)
                throw new ArgumentException("Output UV channel must be in range 0-7.");
            var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + outputChannel);
            if (source.HasVertexAttribute(attribute) && source.GetVertexAttributeDimension(attribute) > 2)
                throw new InvalidDataException("Output channel is Vector3/Vector4. Safe import refuses to discard Z/W.");
            folder = (folder ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + source.name + "_UVX.asset");
            Mesh output = UnityEngine.Object.Instantiate(source);
            output.name = Path.GetFileNameWithoutExtension(path);
            output.SetUVs(outputChannel, new List<Vector2>(targetUv));
            AssetDatabase.CreateAsset(output, path);
            AssetDatabase.SaveAssets();
            if (assign)
            {
                Undo.RecordObject(renderer, "Assign UVX Mesh");
                renderer.sharedMesh = output;
                EditorUtility.SetDirty(renderer);
            }
            Selection.activeObject = output;
            return output;
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets", StringComparison.Ordinal))
                throw new InvalidDataException("Asset folder must be inside Assets.");
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

