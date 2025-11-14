using NetTopologySuite.Geometries;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using TimeIsLife.ViewModel;

namespace TimeIsLife.Model
{
    public static class Kruskal
    {
        private const double MaxDist = 15000;

        public static List<LineString> FindMinimumSpanningTree(List<Point> points, GeometryFactory geometry)
        {
            int n = points.Count;
            var result = new List<LineString>();
            if (n < 2) return result;

            double cell = MaxDist;
            var grid = new Dictionary<(int, int), List<int>>();

            // --- 构建空间网格 ---
            for (int i = 0; i < n; i++)
            {
                var p = points[i];
                int cx = (int)(p.X / cell);
                int cy = (int)(p.Y / cell);
                var key = (cx, cy);

                if (!grid.ContainsKey(key))
                    grid[key] = new List<int>();
                grid[key].Add(i);
            }

            // --- 构建候选边（只检查邻域九宫格） ---
            var edges = new List<(int u, int v, double d)>();

            foreach (var kv in grid)
            {
                var (cx, cy) = kv.Key;

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var key2 = (cx + dx, cy + dy);

                        if (!grid.ContainsKey(key2))
                            continue;

                        foreach (var i in kv.Value)
                        {
                            foreach (var j in grid[key2])
                            {
                                if (i >= j) continue;

                                double d = Distance(points[i], points[j]);
                                if (d <= MaxDist)
                                    edges.Add((i, j, d));
                            }
                        }
                    }
                }
            }

            // 没有边直接返回
            if (edges.Count == 0)
                return result;

            // --- 按距离排序 ---
            edges.Sort((a, b) => a.d.CompareTo(b.d));

            // --- Kruskal 并查集 ---
            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            int Find(int x)
            {
                while (x != parent[x])
                    x = parent[x] = parent[parent[x]];
                return x;
            }

            bool Union(int a, int b)
            {
                a = Find(a);
                b = Find(b);
                if (a == b) return false;
                parent[a] = b;
                return true;
            }

            // --- 生成 MST ---
            foreach (var e in edges)
            {
                if (Union(e.u, e.v))
                {
                    result.Add(geometry.CreateLineString(new[]
                    {
                    new Coordinate(points[e.u].X, points[e.u].Y),
                    new Coordinate(points[e.v].X, points[e.v].Y)
                }));
                }
            }

            return result;
        }

        private static double Distance(Point p1, Point p2)
        {
            double dx = p1.X - p2.X;
            double dy = p1.Y - p2.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

}
