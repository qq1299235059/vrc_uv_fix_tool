using System;
using System.Collections.Generic;
using UnityEngine;

namespace UVStrainBaker
{
    public sealed class SolveInput
    {
        public Vector3[] ReferencePositions;
        public Vector3[] CurrentPositions;
        public Vector2[] SourceUv;
        public int[] Triangles;
        public float Strength = 1;
        public bool WeldCoincident = true;
        public float PositionTolerance;
        public float UvTolerance = 1e-6f;
        public double MinimumStretch = 0.125;
        public double MaximumStretch = 8;
        public double RelativeTolerance = 1e-7;
        public int MaxIterations = 1000;
    }

    public sealed class SolveResult
    {
        public Vector2[] CorrectedUv;
        public UvStrainReport Report;
    }

    public sealed class UvStrainReport
    {
        public int VertexCount, LogicalVertexCount, TriangleCount, IslandCount;
        public int ValidTriangles, ReferenceDegenerate, CurrentDegenerate, SourceUvDegenerate;
        public int FlipCount, IterationsU, IterationsV;
        public double MinStretch, MedianStretch, P95Stretch, MaxStretch;
        public double MeanResidual, P95Residual, MaxResidual;
        public Vector2 UvMinimum, UvMaximum;

        public string Summary =>
            $"Vertices {VertexCount} (logical {LogicalVertexCount}), triangles {TriangleCount} " +
            $"(valid {ValidTriangles}), UV islands {IslandCount}\n" +
            $"Skipped: reference {ReferenceDegenerate}, current {CurrentDegenerate}, source UV {SourceUvDegenerate}\n" +
            $"Principal stretch min/median/p95/max: {MinStretch:F3} / {MedianStretch:F3} / {P95Stretch:F3} / {MaxStretch:F3}\n" +
            $"Edge residual mean/p95/max: {MeanResidual:G3} / {P95Residual:G3} / {MaxResidual:G3}\n" +
            $"UV flips {FlipCount}; PCG iterations U/V {IterationsU}/{IterationsV}\n" +
            $"Output UV bounds: {UvMinimum} to {UvMaximum}";
    }

    public static class UvStrainSolver
    {
        public static SolveResult Solve(SolveInput input)
        {
            Validate(input);
            int count = input.ReferencePositions.Length;
            var report = new UvStrainReport
            {
                VertexCount = count, TriangleCount = input.Triangles.Length / 3
            };
            if (count == 0) throw new ArgumentException("Mesh has no vertices.");
            Bounds bounds = new Bounds(input.ReferencePositions[0], Vector3.zero);
            for (int i = 1; i < count; i++) bounds.Encapsulate(input.ReferencePositions[i]);
            double diagonal = Math.Max(bounds.size.magnitude, 1e-7);
            float positionTolerance = input.PositionTolerance > 0
                ? input.PositionTolerance : (float)Math.Max(diagonal * 1e-5, 1e-7);
            UvTopology topology = UvTopologyBuilder.Build(input.ReferencePositions,
                input.CurrentPositions, input.SourceUv, input.Triangles,
                input.WeldCoincident, positionTolerance, input.UvTolerance);
            report.LogicalVertexCount = topology.LogicalCount;
            List<TriangleSample> samples = TriangleStrainBuilder.Build(
                input.ReferencePositions, input.CurrentPositions, input.SourceUv,
                input.Triangles, topology.LogicalOfVertex, input.Strength,
                input.MinimumStretch, input.MaximumStretch, diagonal, report);
            if (samples.Count == 0)
                throw new InvalidOperationException("No valid triangles remain for UV strain solving.");
            SparseSystem system = SparseSystemBuilder.Build(topology.LogicalCount,
                topology.SourceLogicalUv, samples);
            report.IslandCount = system.IslandCount;
            double[] solvedU = PcgSolver.Solve(system, system.RightU, system.InitialU,
                input.RelativeTolerance, input.MaxIterations, out report.IterationsU);
            double[] solvedV = PcgSolver.Solve(system, system.RightV, system.InitialV,
                input.RelativeTolerance, input.MaxIterations, out report.IterationsV);
            Vector2[] logicalUv = (Vector2[])topology.SourceLogicalUv.Clone();
            for (int i = 0; i < system.FreeToLogical.Length; i++)
                logicalUv[system.FreeToLogical[i]] = new Vector2((float)solvedU[i], (float)solvedV[i]);
            Vector2[] output = new Vector2[count];
            for (int i = 0; i < count; i++) output[i] = logicalUv[topology.LogicalOfVertex[i]];
            Diagnose(samples, logicalUv, output, report);
            return new SolveResult { CorrectedUv = output, Report = report };
        }

        private static void Validate(SolveInput input)
        {
            if (input == null || input.ReferencePositions == null || input.CurrentPositions == null ||
                input.SourceUv == null || input.Triangles == null)
                throw new ArgumentException("Solve input is incomplete.");
            int n = input.ReferencePositions.Length;
            if (input.CurrentPositions.Length != n || input.SourceUv.Length != n ||
                input.Triangles.Length % 3 != 0)
                throw new ArgumentException("Mesh position, UV or triangle counts do not match.");
            if (input.Strength < 0 || input.Strength > 1 ||
                input.MinimumStretch <= 0 || input.MaximumStretch < input.MinimumStretch ||
                input.RelativeTolerance <= 0 || input.MaxIterations < 1 || input.UvTolerance <= 0)
                throw new ArgumentException("Solve settings are invalid.");
            for (int i = 0; i < n; i++)
            {
                if (!Finite(input.ReferencePositions[i]) || !Finite(input.CurrentPositions[i]) ||
                    !Finite(input.SourceUv[i]))
                    throw new ArgumentException("Mesh contains NaN or Infinity.");
            }
            foreach (int index in input.Triangles)
                if (index < 0 || index >= n) throw new ArgumentException("Triangle index is out of range.");
        }

        private static void Diagnose(List<TriangleSample> samples, Vector2[] logical,
            Vector2[] output, UvStrainReport report)
        {
            var stretches = new List<double>(samples.Count * 2);
            var residuals = new List<double>(samples.Count * 3);
            foreach (var sample in samples)
            {
                stretches.Add(sample.MinStretch); stretches.Add(sample.MaxStretch);
                Vector2 a = logical[sample.A], b = logical[sample.B], c = logical[sample.C];
                residuals.Add(((b - a) - sample.D01).magnitude);
                residuals.Add(((c - a) - sample.D02).magnitude);
                residuals.Add(((c - b) - sample.D12).magnitude);
                Vector2 sa = logical[sample.A], sb = sa + sample.D01, sc = sa + sample.D02;
                double targetArea = Cross(sb - sa, sc - sa);
                double outputArea = Cross(b - a, c - a);
                if (Math.Abs(targetArea) > 1e-12 && targetArea * outputArea < 0) report.FlipCount++;
            }
            stretches.Sort(); residuals.Sort();
            report.MinStretch = stretches[0];
            report.MedianStretch = Percentile(stretches, 0.5);
            report.P95Stretch = Percentile(stretches, 0.95);
            report.MaxStretch = stretches[stretches.Count - 1];
            double sum = 0;
            foreach (double value in residuals) sum += value;
            report.MeanResidual = sum / residuals.Count;
            report.P95Residual = Percentile(residuals, 0.95);
            report.MaxResidual = residuals[residuals.Count - 1];
            Vector2 minimum = output[0], maximum = output[0];
            foreach (Vector2 uv in output)
            {
                minimum = Vector2.Min(minimum, uv);
                maximum = Vector2.Max(maximum, uv);
            }
            report.UvMinimum = minimum; report.UvMaximum = maximum;
        }

        private static double Cross(Vector2 a, Vector2 b) => (double)a.x * b.y - (double)a.y * b.x;
        private static double Percentile(List<double> sorted, double p) =>
            sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];
        private static bool Finite(Vector2 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) &&
            !float.IsNaN(p.y) && !float.IsInfinity(p.y);
        private static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) &&
            !float.IsNaN(p.y) && !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);
    }
}

