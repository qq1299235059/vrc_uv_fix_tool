using System;
using System.Collections.Generic;
using UnityEngine;

namespace UVStrainBaker
{
    internal sealed class UvTopology
    {
        public int[] LogicalOfVertex;
        public int LogicalCount;
        public int[] IslandOfLogical;
        public int[] Anchors;
        public Vector2[] SourceLogicalUv;
    }

    internal static class UvTopologyBuilder
    {
        private readonly struct Cell : IEquatable<Cell>
        {
            public readonly long X, Y, Z;
            public Cell(long x, long y, long z) { X = x; Y = y; Z = z; }
            public bool Equals(Cell other) => X == other.X && Y == other.Y && Z == other.Z;
            public override bool Equals(object obj) => obj is Cell other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return ((X.GetHashCode() * 397) ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode(); }
            }
        }

        public static UvTopology Build(Vector3[] reference, Vector3[] current, Vector2[] sourceUv,
            int[] triangles, bool weld, float positionTolerance, float uvTolerance)
        {
            int n = reference.Length;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            if (weld)
            {
                var buckets = new Dictionary<Cell, List<int>>();
                double inverse = 1.0 / Math.Max(positionTolerance, 1e-12f);
                double p2 = (double)positionTolerance * positionTolerance;
                double u2 = (double)uvTolerance * uvTolerance;
                for (int i = 0; i < n; i++)
                {
                    Cell key = Quantize(reference[i], inverse);
                    int match = -1;
                    for (int x = -1; x <= 1 && match < 0; x++)
                    for (int y = -1; y <= 1 && match < 0; y++)
                    for (int z = -1; z <= 1 && match < 0; z++)
                    {
                        if (!buckets.TryGetValue(new Cell(key.X + x, key.Y + y, key.Z + z), out var list))
                            continue;
                        foreach (int j in list)
                        {
                            if ((reference[i] - reference[j]).sqrMagnitude <= p2 &&
                                (current[i] - current[j]).sqrMagnitude <= p2 &&
                                (sourceUv[i] - sourceUv[j]).sqrMagnitude <= u2)
                            {
                                match = j;
                                break;
                            }
                        }
                    }
                    if (match >= 0) parent[i] = Find(parent, match);
                    if (!buckets.TryGetValue(key, out var own)) buckets[key] = own = new List<int>();
                    own.Add(i);
                }
            }

            var rootToLogical = new Dictionary<int, int>();
            var map = new int[n];
            var logicalUv = new List<Vector2>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(parent, i);
                if (!rootToLogical.TryGetValue(root, out int id))
                {
                    id = logicalUv.Count;
                    rootToLogical.Add(root, id);
                    logicalUv.Add(sourceUv[root]);
                }
                map[i] = id;
            }

            int count = logicalUv.Count;
            var graph = new List<int>[count];
            for (int i = 0; i < count; i++) graph[i] = new List<int>();
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = map[triangles[t]], b = map[triangles[t + 1]], c = map[triangles[t + 2]];
                AddEdge(graph, a, b); AddEdge(graph, b, c); AddEdge(graph, c, a);
            }
            var island = new int[count];
            for (int i = 0; i < count; i++) island[i] = -1;
            var anchors = new List<int>();
            var queue = new Queue<int>();
            for (int i = 0; i < count; i++)
            {
                if (island[i] >= 0) continue;
                int id = anchors.Count;
                anchors.Add(i);
                island[i] = id;
                queue.Enqueue(i);
                while (queue.Count != 0)
                {
                    int at = queue.Dequeue();
                    foreach (int next in graph[at])
                    {
                        if (island[next] >= 0) continue;
                        island[next] = id;
                        queue.Enqueue(next);
                    }
                }
            }
            return new UvTopology
            {
                LogicalOfVertex = map, LogicalCount = count, IslandOfLogical = island,
                Anchors = anchors.ToArray(), SourceLogicalUv = logicalUv.ToArray()
            };
        }

        private static Cell Quantize(Vector3 p, double inverse) => new Cell(
            (long)Math.Floor(p.x * inverse), (long)Math.Floor(p.y * inverse),
            (long)Math.Floor(p.z * inverse));

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }

        private static void AddEdge(List<int>[] graph, int a, int b)
        {
            if (a == b) return;
            graph[a].Add(b); graph[b].Add(a);
        }
    }
}

