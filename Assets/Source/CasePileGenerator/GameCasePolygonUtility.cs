using System;
using System.Collections.Generic;
using UnityEngine;

public static class GameCasePolygonUtility
{
    private const float Epsilon = 0.00001f;

    public static float SignedArea(IReadOnlyList<Vector2> points)
    {
        if (points == null || points.Count < 3)
            return 0f;

        double area = 0d;

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Count];
            area += (double)a.x * b.y - (double)b.x * a.y;
        }

        return (float)(area * 0.5d);
    }

    public static float Area(IReadOnlyList<Vector2> points)
    {
        return Mathf.Abs(SignedArea(points));
    }

    public static bool ContainsPoint(IReadOnlyList<Vector2> polygon, Vector2 point)
    {
        if (polygon == null || polygon.Count < 3)
            return false;

        bool inside = false;

        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];

            bool crosses = (a.y > point.y) != (b.y > point.y);

            if (!crosses)
                continue;

            float xAtY =
                (b.x - a.x) * (point.y - a.y) /
                ((b.y - a.y) + Mathf.Sign(b.y - a.y) * Epsilon) + a.x;

            if (point.x < xAtY)
                inside = !inside;
        }

        return inside;
    }

    public static Rect CalculateBounds(IReadOnlyList<Vector2> points)
    {
        if (points == null || points.Count == 0)
            return new Rect();

        float minX = points[0].x;
        float maxX = minX;
        float minY = points[0].y;
        float maxY = minY;

        for (int i = 1; i < points.Count; i++)
        {
            Vector2 point = points[i];
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxY = Mathf.Max(maxY, point.y);
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    public static bool IsSimple(IReadOnlyList<Vector2> points)
    {
        if (points == null || points.Count < 3)
            return false;

        int count = points.Count;

        for (int a = 0; a < count; a++)
        {
            int aNext = (a + 1) % count;

            for (int b = a + 1; b < count; b++)
            {
                int bNext = (b + 1) % count;

                if (a == b || aNext == b || bNext == a)
                    continue;

                if (a == 0 && bNext == 0)
                    continue;

                if (SegmentsIntersect(points[a], points[aNext], points[b], points[bNext]))
                    return false;
            }
        }

        return true;
    }

    public static List<Vector2> SimplifyClosed(IReadOnlyList<Vector2> source, float tolerance)
    {
        var result = source == null
            ? new List<Vector2>()
            : new List<Vector2>(source);

        if (result.Count < 4)
            return result;

        float squaredTolerance = Mathf.Max(0.0001f, tolerance * tolerance);
        bool changed = true;
        int safety = 0;

        while (changed && result.Count > 3 && safety++ < 10000)
        {
            changed = false;

            for (int i = 0; i < result.Count; i++)
            {
                Vector2 previous = result[(i - 1 + result.Count) % result.Count];
                Vector2 current = result[i];
                Vector2 next = result[(i + 1) % result.Count];

                if (DistanceToSegmentSquared(current, previous, next) > squaredTolerance)
                    continue;

                result.RemoveAt(i);
                changed = true;
                break;
            }
        }

        return result;
    }

    public static List<int> Triangulate(IReadOnlyList<Vector2> polygon)
    {
        var triangles = new List<int>();

        if (polygon == null || polygon.Count < 3)
            return triangles;

        int count = polygon.Count;
        var vertices = new List<int>(count);

        if (SignedArea(polygon) > 0f)
        {
            for (int i = 0; i < count; i++)
                vertices.Add(i);
        }
        else
        {
            for (int i = count - 1; i >= 0; i--)
                vertices.Add(i);
        }

        int guard = count * count;

        while (vertices.Count > 2 && guard-- > 0)
        {
            bool clippedEar = false;

            for (int i = 0; i < vertices.Count; i++)
            {
                int previousIndex = vertices[(i - 1 + vertices.Count) % vertices.Count];
                int currentIndex = vertices[i];
                int nextIndex = vertices[(i + 1) % vertices.Count];

                Vector2 a = polygon[previousIndex];
                Vector2 b = polygon[currentIndex];
                Vector2 c = polygon[nextIndex];

                if (Cross(b - a, c - b) <= Epsilon)
                    continue;

                bool containsAnotherVertex = false;

                for (int j = 0; j < vertices.Count; j++)
                {
                    int candidateIndex = vertices[j];

                    if (candidateIndex == previousIndex ||
                        candidateIndex == currentIndex ||
                        candidateIndex == nextIndex)
                    {
                        continue;
                    }

                    if (PointInTriangle(polygon[candidateIndex], a, b, c))
                    {
                        containsAnotherVertex = true;
                        break;
                    }
                }

                if (containsAnotherVertex)
                    continue;

                triangles.Add(previousIndex);
                triangles.Add(currentIndex);
                triangles.Add(nextIndex);
                vertices.RemoveAt(i);
                clippedEar = true;
                break;
            }

            if (!clippedEar)
                break;
        }

        return triangles;
    }

    private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float abC = Cross(b - a, c - a);
        float abD = Cross(b - a, d - a);
        float cdA = Cross(d - c, a - c);
        float cdB = Cross(d - c, b - c);

        if (((abC > Epsilon && abD < -Epsilon) || (abC < -Epsilon && abD > Epsilon)) &&
            ((cdA > Epsilon && cdB < -Epsilon) || (cdA < -Epsilon && cdB > Epsilon)))
        {
            return true;
        }

        return Mathf.Abs(abC) <= Epsilon && OnSegment(a, b, c) ||
               Mathf.Abs(abD) <= Epsilon && OnSegment(a, b, d) ||
               Mathf.Abs(cdA) <= Epsilon && OnSegment(c, d, a) ||
               Mathf.Abs(cdB) <= Epsilon && OnSegment(c, d, b);
    }

    private static bool OnSegment(Vector2 a, Vector2 b, Vector2 point)
    {
        return point.x >= Mathf.Min(a.x, b.x) - Epsilon &&
               point.x <= Mathf.Max(a.x, b.x) + Epsilon &&
               point.y >= Mathf.Min(a.y, b.y) - Epsilon &&
               point.y <= Mathf.Max(a.y, b.y) + Epsilon;
    }

    private static float DistanceToSegmentSquared(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 segment = b - a;
        float lengthSquared = segment.sqrMagnitude;

        if (lengthSquared <= Epsilon)
            return (point - a).sqrMagnitude;

        float t = Mathf.Clamp01(Vector2.Dot(point - a, segment) / lengthSquared);
        Vector2 projection = a + segment * t;
        return (point - projection).sqrMagnitude;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        float c1 = Cross(b - a, point - a);
        float c2 = Cross(c - b, point - b);
        float c3 = Cross(a - c, point - c);

        bool hasNegative = c1 < -Epsilon || c2 < -Epsilon || c3 < -Epsilon;
        bool hasPositive = c1 > Epsilon || c2 > Epsilon || c3 > Epsilon;
        return !(hasNegative && hasPositive);
    }
}
