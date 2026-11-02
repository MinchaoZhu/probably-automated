using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoMod.UI.Kit;

/// <summary>
/// 编辑器用的几何形状贴图，运行时生成：白色填充 + 深色描边（Image.color 着色后描边自动变深）。
/// 节点类型 → 形状：存放 = 圆角矩形，机器 = 六边形，工位 = 椭圆，用电机器 = 菱形，成品 = 箭头形。
/// </summary>
public static class ShapeSprites
{
    private static readonly Dictionary<string, Sprite> Cache = new();

    public const int W = 160, H = 80;

    public static Sprite Node(string type) => Get("node:" + type, W, H, (u, v) => Field(type, u, v), border: 0.09f);

    /// <summary>选中时套在节点外面的光圈（同形状，只有描边）。</summary>
    public static Sprite Halo(string type) => Get("halo:" + type, W, H, (u, v) => Field(type, u, v), border: 0.16f, hollow: true);

    public static Sprite Arrow => Get("arrow", 32, 32, (u, v) => Math.Max(Math.Abs(v) * 2 + u, -u) /* 指向右边的三角 */, border: 0);
    public static Sprite White => Get("white", 4, 4, (u, v) => 0, border: 0);
    public static Sprite Chip => Get("chip", 64, 28, (u, v) => RoundRect(u, v, 0.45f), border: 0.12f);
    public static Sprite Dot => Get("dot", 16, 16, (u, v) => Math.Sqrt(u * u + v * v), border: 0);

    /// <summary>形状的隐式函数：≤ 1 在内部。u, v ∈ [-1, 1]。</summary>
    private static double Field(string type, double u, double v) => type switch
    {
        "storage" => RoundRect(u, v, 0.25f),
        "machine" => Math.Max(Math.Abs(v), Math.Abs(u) + Math.Abs(v) * 0.45), // 左右是尖的六边形
        "consumer" => Math.Abs(u) + Math.Abs(v),
        "product" => Math.Max(Math.Max(Math.Abs(v), -u * 1.0), u * 1.6 - 0.6 + Math.Abs(v) * 1.0),
        _ => Math.Sqrt(u * u + v * v),
    };

    /// <summary>圆角矩形：r 是圆角半径（占半宽的比例）。</summary>
    private static double RoundRect(double u, double v, float r)
    {
        double ax = Math.Abs(u), ay = Math.Abs(v), inner = 1 - r;
        if (ax <= inner || ay <= inner) return Math.Max(ax, ay);
        double dx = ax - inner, dy = ay - inner;
        return inner + Math.Sqrt(dx * dx + dy * dy);
    }

    private static Sprite Get(string key, int w, int h, Func<double, double, double> f, float border, bool hollow = false)
    {
        if (Cache.TryGetValue(key, out var s) && s != null) return s;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        double aa = 2.0 / Math.Min(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double u = (x + 0.5) / w * 2 - 1, v = (y + 0.5) / h * 2 - 1;
                double d = f(u, v);
                double alpha = Math.Clamp((1 - d) / aa, 0, 1);
                double edge = border <= 0 ? 0 : Math.Clamp((d - (1 - border)) / aa, 0, 1); // 1 = 描边
                if (hollow) alpha *= edge;
                byte c = (byte)(255 * (1 - 0.55 * edge * (hollow ? 0 : 1)));
                px[y * w + x] = new Color32(c, c, c, (byte)(255 * alpha));
            }
        tex.SetPixels32(px);
        tex.Apply();
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        s.hideFlags = HideFlags.DontUnloadUnusedAsset;
        Cache[key] = s;
        return s;
    }
}
