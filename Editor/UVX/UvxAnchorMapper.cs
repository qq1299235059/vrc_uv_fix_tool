using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UVStrainBaker
{
    internal sealed class UvxMapping
    {
        public Vector2[] OutputUv;
        public int UnityVertexCount, BlenderLoopCount, TriangleCount, AnchorGroupCount;
        public string Summary =>
            $"Unity vertices: {UnityVertexCount}; Blender loops: {BlenderLoopCount}; triangles: {TriangleCount}\n" +
            $"Matched anchor groups: {AnchorGroupCount}; mapped vertices: {OutputUv.Length}/{UnityVertexCount}\n" +
            "All Blender polygon edges were found in Unity triangles. No vertex splitting is required.";
    }

    internal static class UvxAnchorMapper
    {
        private readonly struct Bucket : IEquatable<Bucket>
        {
            public readonly long X, Y;
            public Bucket(long x, long y) { X = x; Y = y; }
            public bool Equals(Bucket other) => X == other.X && Y == other.Y;
            public override bool Equals(object obj) => obj is Bucket other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return X.GetHashCode() * 397 ^ Y.GetHashCode(); }
            }
        }

        private sealed class Group
        {
            public Vector2 Anchor;
            public Vector2 Target;
            public bool HasTarget;
            public int BlenderCorners;
        }

        public static UvxMapping Map(UvxData data, Mesh mesh, int anchorChannel,
            float anchorTolerance = 1e-5f, float targetTolerance = 1e-6f)
        {
            if (data == null || mesh == null) throw new ArgumentNullException("UVX data and target Mesh are required.");
            if (anchorChannel < 0 || anchorChannel > 7 || anchorTolerance <= 0 || targetTolerance <= 0)
                throw new ArgumentException("UV channel or tolerance is invalid.");
            var attr = (VertexAttribute)((int)VertexAttribute.TexCoord0 + anchorChannel);
            if (!mesh.HasVertexAttribute(attr) || mesh.GetVertexAttributeDimension(attr) < 2)
                throw new InvalidDataException("Unity Anchor UV channel is missing.");
            Vector2[] unityAnchor;
            var triangles = new List<int>();
            using (var array = MeshUtility.AcquireReadOnlyMeshData(mesh))
            {
                var meshData = array[0];
                using (var uv = new NativeArray<Vector2>(meshData.vertexCount, Allocator.Temp))
                {
                    meshData.GetUVs(anchorChannel, uv);
                    unityAnchor = uv.ToArray();
                }
                for (int submesh = 0; submesh < meshData.subMeshCount; submesh++)
                {
                    var desc = meshData.GetSubMesh(submesh);
                    if (desc.topology != MeshTopology.Triangles)
                        throw new InvalidDataException("UVX import requires triangle submeshes.");
                    using (var indices = new NativeArray<int>(desc.indexCount, Allocator.Temp))
                    {
                        meshData.GetIndices(indices, submesh, true);
                        triangles.AddRange(indices.ToArray());
                    }
                }
            }
            if (triangles.Count / 3 != data.TriangleCount)
                throw new InvalidDataException("Triangle counts differ; target Mesh may not match the Blender export.");
            if (unityAnchor.Length == 0) throw new InvalidDataException("Unity Mesh has no vertices.");
            var groups = new List<Group>();
            var buckets = new Dictionary<Bucket, List<int>>();
            int[] unityGroup = new int[unityAnchor.Length];
            for (int i = 0; i < unityAnchor.Length; i++)
            {
                CheckFinite(unityAnchor[i]);
                unityGroup[i] = FindOrCreate(unityAnchor[i]);
            }

            int FindOrCreate(Vector2 uv)
            {
                int existing = Find(uv);
                if (existing >= 0) return existing;
                int id = groups.Count;
                groups.Add(new Group { Anchor = uv });
                Bucket bucket = Quantize(uv, anchorTolerance);
                if (!buckets.TryGetValue(bucket, out var list)) buckets[bucket] = list = new List<int>();
                list.Add(id);
                return id;
            }

            int Find(Vector2 uv)
            {
                Bucket key = Quantize(uv, anchorTolerance);
                int found = -1;
                double tolerance2 = (double)anchorTolerance * anchorTolerance;
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                {
                    if (!buckets.TryGetValue(new Bucket(key.X + x, key.Y + y), out var list)) continue;
                    foreach (int id in list)
                    {
                        if ((groups[id].Anchor - uv).sqrMagnitude > tolerance2) continue;
                        if (found >= 0 && found != id)
                            throw new InvalidDataException("Multiple Unity Anchor groups fall inside one tolerance radius.");
                        found = id;
                    }
                }
                return found;
            }

            var blenderGroup = new int[data.AnchorUv.Length];
            for (int i = 0; i < blenderGroup.Length; i++)
            {
                int id = Find(data.AnchorUv[i]);
                if (id < 0)
                    throw new InvalidDataException("Blender Anchor UV has no Unity match at loop " + i + ".");
                Group group = groups[id];
                Vector2 target = data.TargetUv[i];
                CheckFinite(target);
                if (group.HasTarget && (group.Target - target).sqrMagnitude >
                    (double)targetTolerance * targetTolerance)
                    throw new InvalidDataException("Target UV has a seam or ambiguous overlap at Anchor group " + id +
                        ". This safe importer does not guess or split vertices.");
                group.Target = target;
                group.HasTarget = true;
                group.BlenderCorners++;
                blenderGroup[i] = id;
            }
            for (int i = 0; i < groups.Count; i++)
                if (!groups[i].HasTarget)
                    throw new InvalidDataException("Unity Anchor group " + i + " has no Blender corner match.");

            var unityEdges = new HashSet<ulong>();
            for (int t = 0; t < triangles.Count; t += 3)
            {
                int a = unityGroup[triangles[t]], b = unityGroup[triangles[t + 1]], c = unityGroup[triangles[t + 2]];
                unityEdges.Add(Edge(a, b)); unityEdges.Add(Edge(b, c)); unityEdges.Add(Edge(c, a));
            }
            foreach (UvxPolygon polygon in data.Polygons)
                for (int j = 0; j < polygon.LoopCount; j++)
                {
                    int a = blenderGroup[polygon.FirstLoop + j];
                    int b = blenderGroup[polygon.FirstLoop + (j + 1) % polygon.LoopCount];
                    if (!unityEdges.Contains(Edge(a, b)))
                        throw new InvalidDataException("Blender polygon edge is absent from Unity topology; refusing the mapping.");
                }
            var output = new Vector2[unityAnchor.Length];
            for (int i = 0; i < output.Length; i++) output[i] = groups[unityGroup[i]].Target;
            return new UvxMapping
            {
                OutputUv = output, UnityVertexCount = output.Length,
                BlenderLoopCount = data.AnchorUv.Length, TriangleCount = data.TriangleCount,
                AnchorGroupCount = groups.Count
            };
        }

        private static Bucket Quantize(Vector2 value, float tolerance) => new Bucket(
            (long)Math.Floor(value.x / tolerance), (long)Math.Floor(value.y / tolerance));

        private static ulong Edge(int a, int b)
        {
            uint lo = (uint)Math.Min(a, b), hi = (uint)Math.Max(a, b);
            return ((ulong)lo << 32) | hi;
        }

        private static void CheckFinite(Vector2 uv)
        {
            if (float.IsNaN(uv.x) || float.IsInfinity(uv.x) ||
                float.IsNaN(uv.y) || float.IsInfinity(uv.y))
                throw new InvalidDataException("Anchor or Target UV contains NaN or Infinity.");
        }
    }
}

