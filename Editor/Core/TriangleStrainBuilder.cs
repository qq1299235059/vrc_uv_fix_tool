using System;
using System.Collections.Generic;
using UnityEngine;

namespace UVStrainBaker
{
    internal readonly struct TriangleSample
    {
        public readonly int A, B, C;
        public readonly Vector2 D01, D02, D12;
        public readonly double Area;
        public readonly double MinStretch, MaxStretch;

        public TriangleSample(int a, int b, int c, Vector2 d01, Vector2 d02,
            double area, double minStretch, double maxStretch)
        {
            A = a; B = b; C = c;
            D01 = d01; D02 = d02; D12 = d02 - d01;
            Area = area; MinStretch = minStretch; MaxStretch = maxStretch;
        }
    }

    internal static class TriangleStrainBuilder
    {
        public static List<TriangleSample> Build(Vector3[] reference, Vector3[] current,
            Vector2[] uv, int[] triangles, int[] logical, float strength,
            double minimumStretch, double maximumStretch, double boundsDiagonal,
            UvStrainReport report)
        {
            var samples = new List<TriangleSample>(triangles.Length / 3);
            double areaThreshold = Math.Max(boundsDiagonal * boundsDiagonal * 1e-12, 1e-24);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                Matrix2 x0 = StrainMath.Intrinsic(reference[a], reference[b], reference[c]);
                Matrix2 xc = StrainMath.Intrinsic(current[a], current[b], current[c]);
                Matrix2 u0 = StrainMath.UvEdges(uv[a], uv[b], uv[c]);
                double area = x0.Determinant * 0.5;
                if (area < areaThreshold || logical[a] == logical[b] ||
                    logical[a] == logical[c] || logical[b] == logical[c])
                {
                    report.ReferenceDegenerate++;
                    continue;
                }
                if (xc.Determinant * 0.5 < areaThreshold)
                {
                    report.CurrentDegenerate++;
                    continue;
                }
                if (Math.Abs(u0.Determinant) < 1e-16)
                {
                    report.SourceUvDegenerate++;
                    continue;
                }
                Matrix2 f = xc * x0.Inverse;
                StrainMath.SingularValues(f, out double min, out double max);
                Matrix2 safe = StrainMath.ClampStretch(f, minimumStretch, maximumStretch);
                Matrix2 target = (u0 * x0.Inverse) * (safe * x0);
                Vector2 d01 = Vector2.LerpUnclamped(u0.Column0, target.Column0, strength);
                Vector2 d02 = Vector2.LerpUnclamped(u0.Column1, target.Column1, strength);
                if (!Finite(d01) || !Finite(d02))
                    throw new InvalidOperationException("Computed UV edge is not finite.");
                samples.Add(new TriangleSample(logical[a], logical[b], logical[c],
                    d01, d02, area, min, max));
            }
            report.ValidTriangles = samples.Count;
            return samples;
        }

        private static bool Finite(Vector2 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }
}

