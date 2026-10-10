// Checks the warp matrix in Plugin.WarpMatrix against the projection in Plugin.View.
//
// The 3D renderer draws the playfield flat and lets one projective matrix do the perspective, so the
// matrix is the whole 3D: if it is not exactly the same projection as View.Rise / View.Scale, the
// lanes converge on the wrong point and every note lands in the wrong place. This harness recomputes
// both independently - the rational forms the shader will see, and the rise/scale pair the culling
// and the horizon are written against - and compares them over a grid of points.
//
//   csc.exe /target:exe /out:tools\WarpCheck.exe tools\WarpCheck.cs
//   tools\WarpCheck.exe
//
// It is NOT compiled into the mod (build.ps1 only globs src\LaneReader\*.cs).

using System;

internal static class WarpCheck
{
    private static int _failures;

    // ---- the two projections, copied from Plugin (deliberately, so this file is independent) ----

    /// <summary>Plugin.View.Rise: how far above the judgement line a point d of fall sits.</summary>
    private static double Rise(double d, double len, double eye)
    {
        double u = Math.Max(d + eye, eye * 0.05);
        return len * (u - eye) / u;
    }

    /// <summary>Plugin.View.Scale: what a flat width is multiplied by at that distance.</summary>
    private static double Scale(double d, double eye)
    {
        double u = Math.Max(d + eye, eye * 0.05);
        return eye / u;
    }

    /// <summary>
    /// Plugin.WarpMatrix, as a 3x3 acting on (x, y, 1) - rows (X, Y, W) in that order. The 4x4 the
    /// plugin builds puts the first row in m00/m01/m03, the second in m10/m11/m13 and the third in
    /// m30/m31/m33, with z left at 0 so depth stays in the ordinary 2D case.
    /// </summary>
    private static void Warp(double x, double y, double cx, double judgeY, double len, double eye,
        out double px, out double py)
    {
        double X = eye * x - cx * y + cx * judgeY;
        double Y = judgeY * (judgeY + eye - len) + y * (len - judgeY);
        double W = judgeY + eye - y;
        px = X / W;
        py = Y / W;
    }

    private static void Check(string what, double got, double want, double tolerance)
    {
        double error = Math.Abs(got - want);
        if (error <= tolerance)
            return;
        _failures++;
        Console.WriteLine(string.Format("FAIL {0}: got {1:F6}, want {2:F6} (off by {3:F6})",
            what, got, want, error));
    }

    private static void Main()
    {
        // Lane lengths, strengths (eye = len / strength) and screen geometries, including the
        // extremes the sliders allow.
        double[] lens = { 200.0, 900.0, 1620.0, 3200.0 };
        double[] strengths = { 0.3, 1.0, 2.4936, 3.0 };
        double[] judgeYs = { 120.0, 500.0, 886.0, 1020.0 };
        double[] cxs = { 0.0, 540.0, 960.0, 1920.0, 3840.0 - 480.0 };
        double[] xs = { -200.0, 0.0, 480.0, 960.0, 1500.0, 2000.0 };

        // Fall distances: below the line (a plate's near edge), on it, far up the lane, and far past
        // the lane length (the lane body is drawn to eye*999, so that has to hold up too).
        double[] falls = { -13.0, -1.0, 0.0, 0.5, 13.0, 100.0, 785.0, 1620.0, 5000.0, 1e6 };

        int checkedPoints = 0;
        foreach (double len in lens)
        {
            foreach (double strength in strengths)
            {
                double eye = len / strength;
                foreach (double judgeY in judgeYs)
                {
                    foreach (double cx in cxs)
                    {
                        foreach (double x in xs)
                        {
                            foreach (double d in falls)
                            {
                                double y = judgeY - d;
                                double px, py;
                                Warp(x, y, cx, judgeY, len, eye, out px, out py);

                                // What the flat drawing code + View says the same point is.
                                double wantX = cx + (x - cx) * Scale(d, eye);
                                double wantY = judgeY - Rise(d, len, eye);

                                Check("x", px, wantX, 1e-3);
                                Check("y", py, wantY, 1e-3);
                                checkedPoints++;
                            }
                        }
                    }
                }
            }
        }

        // The two fixed points, spelled out: the judgement line does not move, and the vanishing
        // point is the lane top.
        double l = 1620.0, s = 1.0, e = l / s, jy = 886.0, centre = 960.0;
        double wx, wy;
        Warp(400.0, jy, centre, jy, l, e, out wx, out wy);
        Check("judgement line x", wx, 400.0, 1e-4);
        Check("judgement line y", wy, jy, 1e-4);

        // Not literally at infinity - y = -1e9 is just far enough that the remaining 0.003 px of
        // approach is below anything that could be seen.
        Warp(400.0, -1e9, centre, jy, l, e, out wx, out wy);
        Check("vanishing point x", wx, centre, 1e-3);
        Check("vanishing point y", wy, jy - l, 1e-2);

        Console.WriteLine(string.Format("{0} points checked, {1} failures", checkedPoints, _failures));
        Environment.Exit(_failures == 0 ? 0 : 1);
    }
}
