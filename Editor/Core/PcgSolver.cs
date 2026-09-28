using System;

namespace UVStrainBaker
{
    internal static class PcgSolver
    {
        public static double[] Solve(SparseSystem system, double[] right, double[] initial,
            double relativeTolerance, int maxIterations, out int iterations)
        {
            int n = right.Length;
            var x = (double[])initial.Clone();
            var product = new double[n];
            var residual = new double[n];
            var direction = new double[n];
            var z = new double[n];
            system.Multiply(x, product);
            double normRight = 0, normResidual = 0, rz = 0;
            for (int i = 0; i < n; i++)
            {
                residual[i] = right[i] - product[i];
                z[i] = residual[i] / system.Diagonal[i];
                direction[i] = z[i];
                normRight += right[i] * right[i];
                normResidual += residual[i] * residual[i];
                rz += residual[i] * z[i];
            }
            double threshold = relativeTolerance * Math.Max(Math.Sqrt(normRight), 1);
            iterations = 0;
            if (Math.Sqrt(normResidual) <= threshold) return x;
            for (int k = 0; k < maxIterations; k++)
            {
                system.Multiply(direction, product);
                double denominator = Dot(direction, product);
                if (!(denominator > 0) || double.IsNaN(denominator))
                    throw new InvalidOperationException("PCG encountered a non-positive UV system.");
                double alpha = rz / denominator;
                normResidual = 0;
                for (int i = 0; i < n; i++)
                {
                    x[i] += alpha * direction[i];
                    residual[i] -= alpha * product[i];
                    normResidual += residual[i] * residual[i];
                }
                iterations = k + 1;
                if (Math.Sqrt(normResidual) <= threshold) return x;
                double nextRz = 0;
                for (int i = 0; i < n; i++)
                {
                    z[i] = residual[i] / system.Diagonal[i];
                    nextRz += residual[i] * z[i];
                }
                double beta = nextRz / rz;
                for (int i = 0; i < n; i++) direction[i] = z[i] + beta * direction[i];
                rz = nextRz;
            }
            throw new InvalidOperationException("UV PCG did not converge within " + maxIterations + " iterations.");
        }

        private static double Dot(double[] a, double[] b)
        {
            double result = 0;
            for (int i = 0; i < a.Length; i++) result += a[i] * b[i];
            return result;
        }
    }
}

