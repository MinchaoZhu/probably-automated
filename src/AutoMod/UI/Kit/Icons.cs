using System.Collections.Generic;
using UnityEngine;

namespace AutoMod.UI.Kit;

/// <summary>物品图标：用游戏自己的物品贴图（图集 + 名字），按种类缓存。</summary>
public static class Icons
{
    private static readonly Dictionary<string, Sprite> Cache = new();

    /// <summary>某件物品的图标；取不到为空。</summary>
    public static Sprite Of(GameItem item)
    {
        if (item == null) return null;
        string key = item.spriteAtlasPath + "|" + item.spritePath;
        if (Cache.TryGetValue(key, out var s)) return s;
        try { s = RenderHandler.LoadFromAtlas(item.spriteAtlasPath, item.spritePath); }
        catch { s = null; }
        Cache[key] = s;
        return s;
    }

    /// <summary>某种物品（原型 id）的图标：店里没有这种东西时按原型生成一件来取贴图。</summary>
    public static Sprite Kind(string kind)
    {
        if (string.IsNullOrEmpty(kind)) return null;
        if (Cache.TryGetValue("kind:" + kind, out var s)) return s;
        try { s = Of(DirectoryMaster.Item(kind)); }
        catch { s = null; }
        Cache["kind:" + kind] = s;
        return s;
    }
}
