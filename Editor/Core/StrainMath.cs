using System;
using UnityEngine;

namespace UVStrainBaker
{
    internal readonly struct Matrix2
    {
        public readonly double A, B, C, D;

        public Matrix2(double a, double b, double c, double d)
        {
            A = a; B = b; C = c; D = d;
        }

        public double Determinant => A * D - B * C;
        public Matrix2 Inverse => new Matrix2(D / Determinant, -B / Determinant,
            -C / Determinant, A / Determinant);
        public static Matrix2 operator *(Matrix2 x, Matrix2 y) => new Matrix2(
            x.A * y.A + x.B * y.C, x.A * y.B + x.B * y.D,
            x.C * y.A + x.D * y.C, x.C * y.B + x.D * y.D);
        public Vector2 Column0 => new Vector2((float)A, (float)C);
        public Vector2 Column1 => new Vector2((float)B, (float)D);
    }

    internal static class StrainMath
    {
        public static Matrix2 Intrinsic(Vector3 p0, Vector3 p1, Vector3 p2)
        {
            Vector3 e1 = p1 - p0, e2 = p2 - p0;
            double x1 = e1.magnitude;
            if (x1 == 0) return new Matrix2(0, 0, 0, 0);
            double x2 = Vector3.Dot(e1, e2) / x1;
            double y2 = Math.Sqrt(Math.Max(0, (double)e2.sqrMagnitude - x2 * x2));
            return new Matrix2(x1, x2, 0, y2);
        }

        public static Matrix2 UvEdges(Vector2 u0, Vector2 u1, Vector2 u2) => new Matrix2(
            u1.x - u0.x, u2.x - u0.x, u1.y - u0.y, u2.y - u0.y);

        public static void SingularValues(Matrix2 f, out double minimum, out double maximum)
        {
            double a = f.A * f.A + f.C * f.C;
            double b = f.A * f.B + f.C * f.D;
            double d = f.B * f.B + f.D * f.D;
            double halfTrace = (a + d) * 0.5;
            double radius = Math.Sqrt(Math.Max(0, (a - d) * (a - d) * 0.25 + b * b));
            minimum = Math.Sqrt(Math.Max(0, halfTrace - radius));
            maximum = Math.Sqrt(Math.Max(0, halfTrace + radius));
        }

        // Clamp singular values through the right singular vectors. F may include a reflection.
        public static Matrix2 ClampStretch(Matrix2 f, double minimum, double maximum)
        {
            double a = f.A * f.A + f.C * f.C;
            double b = f.A * f.B + f.C * f.D;
            double d = f.B * f.B + f.D * f.D;
            double angle = 0.5 * Math.Atan2(2 * b, a - d);
            double c = Math.Cos(angle), s = Math.Sin(angle);
            // v0=(c,s) is the maximum eigenvector; v1=(-s,c) is the minimum.
            double high = Math.Sqrt(Math.Max(0, a * c * c + 2 * b * c * s + d * s * s));
            double low = Math.Sqrt(Math.Max(0, a * s * s - 2 * b * c * s + d * c * c));
            double highFactor = high > 1e-12
                ? Math.Max(minimum, Math.Min(maximum, high)) / high : 0;
            double lowFactor = low > 1e-12
                ? Math.Max(minimum, Math.Min(maximum, low)) / low : 0;
            // A zero singular direction has no defined orientation. It stays zero; the caller
            // skips nearly collapsed current triangles before reaching this method.
            double m00 = highFactor * c * c + lowFactor * s * s;
            double m01 = (highFactor - lowFactor) * c * s;
            double m11 = highFactor * s * s + lowFactor * c * c;
            return f * new Matrix2(m00, m01, m01, m11);
        }
    }
}

