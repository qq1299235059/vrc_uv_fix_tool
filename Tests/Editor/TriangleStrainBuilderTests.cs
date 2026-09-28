#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace UVStrainBaker.Tests
{
    public sealed class TriangleStrainBuilderTests
    {
        private static SolveResult Solve(Vector3[] current)
        {
            return UvStrainSolver.Solve(new SolveInput
            {
                ReferencePositions = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
                CurrentPositions = current,
                SourceUv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1) },
                Triangles = new[] { 0, 1, 2 },
                WeldCoincident = false
            });
        }

        [Test] public void IdentityPreservesUv()
        {
            var result = Solve(new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) });
            Assert.That(result.CorrectedUv[1].x, Is.EqualTo(1).Within(1e-5));
            Assert.That(result.CorrectedUv[2].y, Is.EqualTo(1).Within(1e-5));
        }

        [Test] public void XStretchExpandsTargetEdge()
        {
            var result = Solve(new[] { new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(0, 1, 0) });
            Assert.That(result.CorrectedUv[1].x, Is.EqualTo(2).Within(1e-4));
        }

        [Test] public void UniformScaleExpandsBothDirections()
        {
            var result = Solve(new[] { Vector3.zero, new Vector3(2, 0, 0), new Vector3(0, 2, 0) });
            Assert.That(result.CorrectedUv[1].x, Is.EqualTo(2).Within(1e-4));
            Assert.That(result.CorrectedUv[2].y, Is.EqualTo(2).Within(1e-4));
        }

        [Test] public void CompressionShrinksTargetEdge()
        {
            var result = Solve(new[] { Vector3.zero, new Vector3(.5f, 0, 0), new Vector3(0, 1, 0) });
            Assert.That(result.CorrectedUv[1].x, Is.EqualTo(.5f).Within(1e-4));
        }

        [Test] public void ShearChangesTheSecondEdge()
        {
            var result = Solve(new[] { Vector3.zero, new Vector3(1, 0, 0), new Vector3(.5f, 1, 0) });
            Assert.That(result.CorrectedUv[2].x, Is.EqualTo(.5f).Within(1e-4));
        }

        [Test] public void RigidRotationDoesNotChangeUv()
        {
            Quaternion q = Quaternion.Euler(0, 0, 55);
            var result = Solve(new[] { q * new Vector3(0, 0, 0), q * new Vector3(1, 0, 0), q * new Vector3(0, 1, 0) });
            Assert.That(result.CorrectedUv[1].x, Is.EqualTo(1).Within(1e-4));
            Assert.That(result.CorrectedUv[2].y, Is.EqualTo(1).Within(1e-4));
        }

        [Test] public void WeldsCoincidentDuplicateVerticesWithSameUv()
        {
            var reference = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 0) };
            var current = (Vector3[])reference.Clone();
            var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(0, 0) };
            var result = UvStrainSolver.Solve(new SolveInput { ReferencePositions = reference, CurrentPositions = current, SourceUv = uv, Triangles = new[] { 0, 1, 2 }, WeldCoincident = true });
            Assert.That(result.Report.LogicalVertexCount, Is.EqualTo(3));
        }

        [Test] public void KeepsCoincidentVerticesWithDifferentUvAsASeam()
        {
            var reference = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.zero };
            var uv = new[] { Vector2.zero, Vector2.right, Vector2.up, new Vector2(.5f, .5f) };
            var result = UvStrainSolver.Solve(new SolveInput
            {
                ReferencePositions = reference, CurrentPositions = reference, SourceUv = uv,
                Triangles = new[] { 0, 1, 2 }, WeldCoincident = true
            });
            Assert.That(result.Report.LogicalVertexCount, Is.EqualTo(4));
        }

        [Test] public void SharedEdgeRemainsContinuous()
        {
            var reference = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
            var current = new[] { Vector3.zero, new Vector3(1.5f, 0, 0), Vector3.up, new Vector3(1.5f, 1, 0) };
            var uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            var result = UvStrainSolver.Solve(new SolveInput
            {
                ReferencePositions = reference, CurrentPositions = current, SourceUv = uv,
                Triangles = new[] { 0, 1, 2, 1, 3, 2 }, WeldCoincident = false
            });
            Assert.That(result.Report.ValidTriangles, Is.EqualTo(2));
            Assert.That(result.CorrectedUv[1].x, Is.EqualTo(1.5f).Within(1e-4));
        }
    }
}
#endif

