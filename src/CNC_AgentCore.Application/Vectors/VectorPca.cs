// Application/Vectors/VectorPca.cs —— 批量向量 PCA 投影到 2D。
// 用 n×n Gram 矩阵 Xc·Xcᵀ 的特征分解代替 d×d 协方差（n 远小于 d 时更快，数学等价）：
//   中心化 → Gram 分解 → 前 2 主成分得分 = v_k·sqrt(λ_k)，解释方差比 = λ_k/Σλ。
using System;
using System.Collections.Generic;

namespace CNC_AgentCore.Application.Vectors;

public static class VectorPca
{
    /// <summary>把一组等长向量按输入顺序 PCA 投影到 2D。</summary>
    public static (double[] Xs, double[] Ys, double Explained0, double Explained1) Project2D(
        IReadOnlyList<float[]> vectors)
    {
        int n = vectors.Count;
        var xs = new double[n];
        var ys = new double[n];
        if (n == 0) return (xs, ys, 0, 0);
        if (n == 1) return (xs, ys, 1.0, 0.0); // 单点无方差方向，置于原点

        int d = vectors[0].Length;

        // 1) 按列中心化
        var mean = new double[d];
        for (int i = 0; i < n; i++)
        {
            var v = vectors[i];
            for (int k = 0; k < d; k++) mean[k] += v[k];
        }
        for (int k = 0; k < d; k++) mean[k] /= n;

        // 2) Gram 矩阵 G = Xc · Xcᵀ（实对称）
        var gram = new double[n][];
        for (int i = 0; i < n; i++) gram[i] = new double[n];
        for (int i = 0; i < n; i++)
        {
            var vi = vectors[i];
            for (int j = i; j < n; j++)
            {
                var vj = vectors[j];
                double s = 0;
                for (int k = 0; k < d; k++)
                    s += (vi[k] - mean[k]) * (vj[k] - mean[k]);
                gram[i][j] = s;
                gram[j][i] = s;
            }
        }

        // 3) Jacobi 特征分解 → (特征值降序, 特征向量按列存放 evecs[i][k]=第 i 行/点、第 k 主成分分量)
        var (evals, evecs) = SymmetricEig(gram);

        // 4) 得分与解释方差
        double total = 0;
        for (int k = 0; k < n; k++) total += evals[k];
        double t = total > 0 ? total : 1.0;
        double s0 = Math.Sqrt(Math.Max(0, evals[0]));
        double s1 = n > 1 ? Math.Sqrt(Math.Max(0, evals[1])) : 0;
        double e0 = Math.Max(0, evals[0]) / t;
        double e1 = n > 1 ? Math.Max(0, evals[1]) / t : 0;
        for (int i = 0; i < n; i++)
        {
            xs[i] = evecs[i][0] * s0;
            ys[i] = evecs[i][1] * s1;
        }
        return (xs, ys, e0, e1);
    }

    /// <summary>实对称矩阵 Jacobi 特征分解。返回特征值降序的 evals，及 evecs[i][k] = 点 i 在第 k 特征向量上的分量。</summary>
    private static (double[] Evals, double[][] Evecs) SymmetricEig(double[][] a)
    {
        int n = a.Length;
        var m = new double[n][];
        var v = new double[n][];
        for (int i = 0; i < n; i++)
        {
            m[i] = new double[n];
            v[i] = new double[n];
            for (int j = 0; j < n; j++) m[i][j] = a[i][j];
            v[i][i] = 1.0;
        }

        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n - 1; p++)
                for (int q = p + 1; q < n; q++)
                    off += m[p][q] * m[p][q];
            if (off < 1e-24) break;

            for (int p = 0; p < n - 1; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    double apq = m[p][q];
                    if (Math.Abs(apq) < 1e-300) continue;
                    double app = m[p][p], aqq = m[q][q];
                    double theta = (aqq - app) / (2 * apq);
                    double tsign = theta >= 0 ? 1.0 : -1.0;
                    double tan = tsign / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    double c = 1 / Math.Sqrt(tan * tan + 1);
                    double s = tan * c;

                    // A ← Gᵀ·A·G；对称矩阵只需旋转行 k≠p,q 再镜像列，p/q 对角显式更新
                    for (int k = 0; k < n; k++)
                    {
                        if (k == p || k == q) continue;
                        double akp = m[k][p], akq = m[k][q];
                        double nkp = c * akp - s * akq;
                        double nkq = s * akp + c * akq;
                        m[k][p] = nkp; m[p][k] = nkp;
                        m[k][q] = nkq; m[q][k] = nkq;
                    }
                    double npp = c * c * app - 2 * s * c * apq + s * s * aqq;
                    double nqq = s * s * app + 2 * s * c * apq + c * c * aqq;
                    m[p][p] = npp; m[q][q] = nqq;
                    m[p][q] = 0; m[q][p] = 0;

                    // 特征向量 V ← V·G（旋转第 p/q 分量）
                    for (int k = 0; k < n; k++)
                    {
                        double vkp = v[k][p], vkq = v[k][q];
                        v[k][p] = c * vkp - s * vkq;
                        v[k][q] = s * vkp + c * vkq;
                    }
                }
            }
        }

        var evals = new double[n];
        for (int i = 0; i < n; i++) evals[i] = m[i][i];

        // 降序排序（交换特征值并交换 v 的对应列）
        for (int i = 0; i < n; i++)
        {
            int best = i;
            for (int j = i + 1; j < n; j++)
                if (evals[j] > evals[best]) best = j;
            if (best != i)
            {
                (evals[i], evals[best]) = (evals[best], evals[i]);
                for (int r = 0; r < n; r++) (v[r][i], v[r][best]) = (v[r][best], v[r][i]);
            }
        }
        return (evals, v);
    }
}
