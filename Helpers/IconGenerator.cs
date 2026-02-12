using Avalonia;
using Avalonia.Media.Imaging;

namespace FindAll.Helpers;

public static class IconGenerator
{
    public static WriteableBitmap CreateAppIcon(int size = 256)
    {
        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);

        using var buf = bitmap.Lock();
        unsafe
        {
            var ptr = (uint*)buf.Address;
            double s = size;
            double cx = s / 2, cy = s / 2;
            double aa = 1.3;

            // Lens center offset slightly up-left
            double lcx = cx - s * 0.06, lcy = cy - s * 0.06;
            double outerR = s * 0.27;
            double innerR = s * 0.21;

            // Handle
            double hAngle = Math.PI / 4;
            double cosA = Math.Cos(hAngle), sinA = Math.Sin(hAngle);
            double hStart = outerR * 0.88;
            double hEnd = outerR + s * 0.24;
            double hHW = s * 0.048; // half-width

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // --- Background rounded rect ---
                    double cornerR = s * 0.22;
                    double edgeX = Math.Max(0, Math.Abs(x - cx) - (cx - cornerR));
                    double edgeY = Math.Max(0, Math.Abs(y - cy) - (cy - cornerR));
                    double rectDist = Math.Sqrt(edgeX * edgeX + edgeY * edgeY) - cornerR;

                    if (rectDist > aa) { ptr[y * size + x] = 0; continue; }
                    double bgAlpha = Smooth(aa, -aa, rectDist);

                    // Background gradient
                    double gt = (double)y / s;
                    double bgR = Mix(60, 22, gt);
                    double bgG = Mix(130, 50, gt);
                    double bgB = Mix(246, 160, gt);

                    // Subtle radial glow at top-center
                    double glowDist = Math.Sqrt((x - cx) * (x - cx) + (y - cy * 0.65) * (y - cy * 0.65));
                    double glow = Smooth(s * 0.55, 0, glowDist) * 0.18;
                    bgR = Math.Min(255, bgR + glow * 80);
                    bgG = Math.Min(255, bgG + glow * 100);
                    bgB = Math.Min(255, bgB + glow * 40);

                    double r = bgR, g = bgG, b = bgB;

                    // Relative to lens center
                    double ldx = x - lcx, ldy = y - lcy;
                    double ldist = Math.Sqrt(ldx * ldx + ldy * ldy);

                    // Project onto handle axis
                    double along = ldx * cosA + ldy * sinA;
                    double perp = -ldx * sinA + ldy * cosA;
                    double absPerp = Math.Abs(perp);

                    // --- Drop shadow ---
                    double sh = s * 0.018;
                    double sx = x - (lcx + sh), sy = y - (lcy + sh);
                    double sd = Math.Sqrt(sx * sx + sy * sy);
                    double shadowLens = Smooth(aa * 4, -aa, sd - outerR - s * 0.008) * 0.30;

                    double sa = sx * cosA + sy * sinA;
                    double sp = Math.Abs(-sx * sinA + sy * cosA);
                    double shadowHandle = 0;
                    if (sa > hStart)
                    {
                        double capD = Math.Sqrt((sa - hEnd) * (sa - hEnd) + sp * sp);
                        double bodyIn = (sa <= hEnd) ? hHW + s * 0.008 - sp : hHW + s * 0.008 - capD;
                        shadowHandle = Smooth(-aa, aa * 4, bodyIn) * Smooth(hStart - aa, hStart + aa, sa) * 0.25;
                    }
                    double shadow = Math.Max(shadowLens, shadowHandle);
                    r *= (1 - shadow); g *= (1 - shadow); b *= (1 - shadow);

                    // --- Handle ---
                    double handleSdf;
                    if (along >= hStart && along <= hEnd)
                        handleSdf = hHW - absPerp;
                    else if (along > hEnd)
                        handleSdf = hHW - Math.Sqrt((along - hEnd) * (along - hEnd) + perp * perp);
                    else
                        handleSdf = -999;

                    double handleA = Smooth(-aa, aa, handleSdf);
                    if (handleA > 0)
                    {
                        double ht = Clamp01((along - hStart) / (hEnd - hStart));
                        // 3D shading: lighter in center, darker at edges
                        double shade = 0.6 + 0.4 * (1 - (absPerp / hHW) * (absPerp / hHW));
                        // Gradient along handle: bright to darker
                        double hR = (Mix(240, 180, ht) * shade + 50 * (1 - shade));
                        double hG = (Mix(240, 180, ht) * shade + 55 * (1 - shade));
                        double hB = (Mix(245, 195, ht) * shade + 65 * (1 - shade));
                        // Top-edge highlight
                        if (perp < 0)
                        {
                            double edgeHL = Smooth(hHW, hHW * 0.6, absPerp) * 0.3;
                            hR = Math.Min(255, hR + edgeHL * 60);
                            hG = Math.Min(255, hG + edgeHL * 60);
                            hB = Math.Min(255, hB + edgeHL * 40);
                        }
                        r = Mix(r, Clamp(hR, 0, 255), handleA);
                        g = Mix(g, Clamp(hG, 0, 255), handleA);
                        b = Mix(b, Clamp(hB, 0, 255), handleA);
                    }

                    // --- Ring ---
                    double ringOuter = outerR - ldist;
                    double ringInner = ldist - innerR;
                    double ringSdf = Math.Min(ringOuter, ringInner);
                    double ringA = Smooth(-aa, aa, ringSdf);

                    if (ringA > 0 && ldist >= innerR - aa && ldist <= outerR + aa)
                    {
                        // Ring: white with subtle 3D bevel
                        double bevel = Smooth(0, 4, ringSdf);
                        // Directional light from top-left
                        double lightAngle = Math.Atan2(ldy, ldx);
                        double lightDir = Math.Cos(lightAngle + Math.PI * 0.75) * 0.12 + 0.88;
                        double rR = Clamp(Mix(195, 255, bevel) * lightDir, 0, 255);
                        double rG = Clamp(Mix(200, 255, bevel) * lightDir, 0, 255);
                        double rB = Clamp(Mix(210, 255, bevel) * lightDir, 0, 255);
                        r = Mix(r, rR, ringA);
                        g = Mix(g, rG, ringA);
                        b = Mix(b, rB, ringA);
                    }

                    // --- Glass ---
                    double glassSdf = innerR - ldist;
                    double glassA = Smooth(-aa, aa, glassSdf);
                    if (glassA > 0)
                    {
                        // Base glass: lighter version of background
                        double glR = Mix(bgR, 190, 0.45);
                        double glG = Mix(bgG, 215, 0.55);
                        double glB = Mix(bgB, 255, 0.35);

                        // Radial gradient in glass: center brighter
                        double glassFrac = ldist / innerR;
                        glR = Mix(glR + 15, glR, glassFrac);
                        glG = Mix(glG + 20, glG, glassFrac);
                        glB = Mix(glB + 10, glB, glassFrac);

                        // Primary highlight: crescent in top-left
                        double hlCx = lcx - innerR * 0.28, hlCy = lcy - innerR * 0.30;
                        double hlDist = Math.Sqrt((x - hlCx) * (x - hlCx) + (y - hlCy) * (y - hlCy));
                        double hl = Smooth(innerR * 0.55, 0, hlDist);
                        hl *= hl;
                        glR = Mix(glR, 255, hl * 0.85);
                        glG = Mix(glG, 255, hl * 0.85);
                        glB = Mix(glB, 255, hl * 0.65);

                        // Small specular dot
                        double specCx = lcx - innerR * 0.35, specCy = lcy - innerR * 0.38;
                        double specDist = Math.Sqrt((x - specCx) * (x - specCx) + (y - specCy) * (y - specCy));
                        double spec = Smooth(innerR * 0.15, 0, specDist);
                        spec = spec * spec * spec;
                        glR = Mix(glR, 255, spec * 0.9);
                        glG = Mix(glG, 255, spec * 0.9);
                        glB = Mix(glB, 255, spec * 0.8);

                        double totalGlassA = glassA * (0.50 + hl * 0.35 + spec * 0.15);
                        totalGlassA = Math.Min(totalGlassA, 0.95);

                        r = Mix(r, Clamp(glR, 0, 255), totalGlassA);
                        g = Mix(g, Clamp(glG, 0, 255), totalGlassA);
                        b = Mix(b, Clamp(glB, 0, 255), totalGlassA);
                    }

                    // --- Final pixel (premultiplied alpha) ---
                    double fa = Clamp(bgAlpha, 0, 1);
                    byte pA = (byte)(fa * 255);
                    byte pR = (byte)(Clamp(r, 0, 255) * fa);
                    byte pG = (byte)(Clamp(g, 0, 255) * fa);
                    byte pB = (byte)(Clamp(b, 0, 255) * fa);

                    ptr[y * size + x] = ((uint)pA << 24) | ((uint)pR << 16) | ((uint)pG << 8) | pB;
                }
            }
        }
        return bitmap;
    }

    public static Avalonia.Controls.WindowIcon CreateWindowIcon()
    {
        var bitmap = CreateAppIcon(64);
        using var ms = new MemoryStream();
        bitmap.Save(ms);
        ms.Position = 0;
        return new Avalonia.Controls.WindowIcon(ms);
    }

    public static void SaveToFile(string path, int size = 256)
    {
        var bitmap = CreateAppIcon(size);
        using var fs = File.Create(path);
        bitmap.Save(fs);
    }

    private static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;
    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    private static double Mix(double a, double b, double t) => a + (b - a) * Clamp01(t);
    private static double Smooth(double edge0, double edge1, double x)
    {
        double t = Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3 - 2 * t);
    }
}
