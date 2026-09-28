using System;
using System.Collections.Generic;
using UnityEngine;

namespace UVStrainBaker
{
    internal sealed class SparseSystem
    {
        public int[] RowStart, Column;
        public double[] Value, Diagonal, RightU, RightV, InitialU, InitialV;
        public int[] FreeToLogical;
        public bool[] IsAnchor;
        public int IslandCount;

        public void Multiply(double[] x, double[] result)
        {
            for (int row = 0; row < FreeToLogical.Length; row++)
            {
                double sum = 0;
                for (int k = RowStart[row]; k < RowStart[row + 1]; k++)
                    sum += Value[k] * x[Column[k]];
                result[row] = sum;
            }
        }
    }

    internal static class SparseSystemBuilder
    {
        public static SparseSystem Build(int logicalCount, Vector2[] source,
            List<TriangleSample> samples)
        {
            var rows = new Dictionary<int, double>[logicalCount];
            var u = new double[logicalCount];
            var v = new double[logicalCount];
            var parent = new int[logicalCount];
            for (int i = 0; i < logicalCount; i++)
            {
                rows[i] = new Dictionary<int, double>();
                parent[i] = i;
            }
            double meanArea = 0;
            foreach (var sample in samples) meanArea += sample.Area;
            meanArea = samples.Count == 0 ? 1 : meanArea / samples.Count;
            foreach (var sample in samples)
            {
                double weight = Math.Max(0.1, Math.Min(10, sample.Area / meanArea));
                Add(sample.A, sample.B, sample.D01, weight);
                Add(sample.A, sample.C, sample.D02, weight);
                Add(sample.B, sample.C, sample.D12, weight);
            }

            void Add(int a, int b, Vector2 delta, double weight)
            {
                if (a == b) return;
                Union(parent, a, b);
                Accumulate(rows[a], a, weight); Accumulate(rows[b], b, weight);
                Accumulate(rows[a], b, -weight); Accumulate(rows[b], a, -weight);
                u[a] -= weight * delta.x; u[b] += weight * delta.x;
                v[a] -= weight * delta.y; v[b] += weight * delta.y;
            }

            var anchors = new bool[logicalCount];
            var roots = new HashSet<int>();
            for (int i = 0; i < logicalCount; i++)
            {
                int root = Find(parent, i);
                if (roots.Add(root)) anchors[i] = true;
            }
            var free = new List<int>();
            var logicalToFree = new int[logicalCount];
            for (int i = 0; i < logicalCount; i++)
            {
                logicalToFree[i] = anchors[i] ? -1 : free.Count;
                if (!anchors[i]) free.Add(i);
            }
            var rowStart = new int[free.Count + 1];
            var columns = new List<int>();
            var values = new List<double>();
            var diagonal = new double[free.Count];
            var rhsU = new double[free.Count];
            var rhsV = new double[free.Count];
            var initialU = new double[free.Count];
            var initialV = new double[free.Count];
            for (int i = 0; i < free.Count; i++)
            {
                int logical = free[i];
                double bu = u[logical], bv = v[logical];
                foreach (var entry in rows[logical])
                {
                    int other = entry.Key;
                    if (anchors[other])
                    {
                        bu -= entry.Value * source[other].x;
                        bv -= entry.Value * source[other].y;
                    }
                    else
                    {
                        columns.Add(logicalToFree[other]);
                        values.Add(entry.Value);
                        if (other == logical) diagonal[i] = entry.Value;
                    }
                }
                if (diagonal[i] <= 0) throw new InvalidOperationException("UV system is singular.");
                rhsU[i] = bu; rhsV[i] = bv;
                initialU[i] = source[logical].x; initialV[i] = source[logical].y;
                rowStart[i + 1] = columns.Count;
            }
            return new SparseSystem
            {
                RowStart = rowStart, Column = columns.ToArray(), Value = values.ToArray(),
                Diagonal = diagonal, RightU = rhsU, RightV = rhsV,
                InitialU = initialU, InitialV = initialV,
                FreeToLogical = free.ToArray(), IsAnchor = anchors, IslandCount = roots.Count
            };
        }

        private static void Accumulate(Dictionary<int, double> row, int column, double value)
        {
            row.TryGetValue(column, out double old);
            row[column] = old + value;
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a), rb = Find(parent, b);
            if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }
    }
}

