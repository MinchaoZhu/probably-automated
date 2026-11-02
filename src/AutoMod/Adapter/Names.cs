using System;
using System.Collections.Generic;

namespace AutoMod.Adapter;

/// <summary>界面上的中文名称：种类名（运行时从游戏物品上学到）、目录以外的属性名。</summary>
public static class Names
{
    /// <summary>种类的中文名：第一次见到某种物品时记录其显示名。</summary>
    private static readonly Dictionary<string, string> KindNames = new();

    /// <summary>血清原型对应游戏 Mechanic 表中的液体名。</summary>
    private static readonly Dictionary<string, string> SerumLiquids = new(StringComparer.OrdinalIgnoreCase)
    {
        ["serum_red"] = "livestock_steroid",
        ["serum_green"] = "livestock_immunity",
    };

    public static void ClearCache()
    {
        KindNames.Clear();
        _learnedAll = false;
    }

    public static void LearnKind(GameItem item)
    {
        try
        {
            if (item == null || string.IsNullOrEmpty(item.identifier)) return;

            // 1. mod 物品的显示名永远按当前语言取表，不记存档里的名字
            if (item.identifier == "probably_automated_intercom" || item.identifier == "probably_automated_handbook" || item.identifier == "probably_automated_daily")
            {
                KindNames[item.identifier] = Kind(item.identifier);
                return;
            }

            // 2. 优先查游戏自己的物品名（Item 表）
            string itemKey = "item_" + item.identifier + "_name";
            if (LocBridge.TryGetGameString("Item", itemKey, out var loc))
            {
                KindNames[item.identifier] = loc;
                return;
            }
            if (LocBridge.TryGetGameString("Item", "husbandry_" + item.identifier + "_name", out var husLoc))
            {
                KindNames[item.identifier] = husLoc;
                return;
            }
            if (LocBridge.TryGetGameString("Item", item.identifier + "_name", out var dirLoc))
            {
                KindNames[item.identifier] = dirLoc;
                return;
            }

            // 3. 退回 item.name 之前先试游戏的液体名
            if (TryGetSerumOrLiquidName(item.identifier, out var liqLoc))
            {
                KindNames[item.identifier] = liqLoc;
                return;
            }

            // 4. mod 本地化表
            if (Domain.L.Has("names.kind." + item.identifier))
            {
                KindNames[item.identifier] = Domain.L.T("names.kind." + item.identifier);
                return;
            }
            if (Domain.L.Has("kind." + item.identifier))
            {
                string t = Domain.L.T("kind." + item.identifier);
                if (LocBridge.IsValidGameLoc(t, "kind." + item.identifier) && t != item.identifier)
                {
                    KindNames[item.identifier] = t;
                    return;
                }
            }

            // 5. 试着读原型名
            if (GameApi.InStore && ItemFacts.DirectoryOf.ContainsKey(item.identifier))
            {
                var proto = ItemFacts.CreatePrototype(item.identifier);
                if (proto != null)
                {
                    string pn = null;
                    try { pn = proto.name; } catch { }
                    try { proto.Destroy(); } catch { }
                    if (IsValidKindName(pn, item.identifier))
                    {
                        KindNames[item.identifier] = pn;
                        return;
                    }
                }
            }

            // 6. 最后退回 item.name（但若不是 zh/ja 且 item.name 含汉字，则不能作为当前语言的名字）
            if (!KindNames.ContainsKey(item.identifier))
            {
                string n = item.name;
                if (IsValidKindName(n, item.identifier)) KindNames[item.identifier] = n;
                else KindNames[item.identifier] = item.identifier;
            }
        }
        catch { }
    }

    private static bool _learnedAll;

    /// <summary>全部物品原型的中文名（第一次调用时逐个创建原型读名字，约 500 种；只能在店里调用）。</summary>
    public static void LearnAll()
    {
        if (_learnedAll || !GameApi.InStore) return;
        foreach (var id in ItemFacts.DirectoryOf.Keys)
        {
            if (KindNames.ContainsKey(id)) continue;
            var proto = ItemFacts.CreatePrototype(id);
            if (proto == null) continue;
            try
            {
                string n = proto.name;
                if (IsValidKindName(n, id)) KindNames[id] = n;
            }
            catch { }
            try { proto.Destroy(); } catch { }
        }
        _learnedAll = true;
    }

    public static string Kind(string id)
    {
        if (string.IsNullOrEmpty(id)) return id;

        // 1. 先判断是不是 mod 物品 id（ModItems.Intercom/Handbook/Daily，赠送的蓝色标签打印机用游戏自己的 labeler 名）
        if (id == "probably_automated_intercom" || id == "intercom") return Domain.L.T("item.intercom.name");
        if (id == "probably_automated_handbook" || id == "handbook") return Domain.L.T("item.handbook.name");
        if (id == "probably_automated_daily") return Domain.L.T("item.daily.name");

        // 2. 优先查游戏自己的物品名（Item 表 item_<id>_name，跟当前游戏语言）
        string itemKey = "item_" + id + "_name";
        if (LocBridge.TryGetGameString("Item", itemKey, out var gameLoc))
            return gameLoc;

        // 2b. 畜牧/机械等在 Item 表中的特殊前缀条目
        if (LocBridge.TryGetGameString("Item", "husbandry_" + id + "_name", out var husLoc))
            return husLoc;
        if (LocBridge.TryGetGameString("Item", id + "_name", out var directLoc))
            return directLoc;

        // 3. 查游戏液体名与血清名（Mechanic 表 liquid_<id>，退回 item.name 之前先试游戏的液体名）
        if (TryGetSerumOrLiquidName(id, out var liqLoc))
            return liqLoc;

        // 4. 检查 mod 本地化表（只给游戏里没有的名字）
        if (Domain.L.Has("names.kind." + id)) return Domain.L.T("names.kind." + id);
        if (Domain.L.Has("kind." + id))
        {
            string t = Domain.L.T("kind." + id);
            if (LocBridge.IsValidGameLoc(t, "kind." + id) && t != id) return t;
        }

        // 5. 检查已学到的名字（若当前非 zh/ja 且包含汉字，则判定为过时的存档中文名，不采用）
        if (KindNames.TryGetValue(id, out var n) && IsValidKindName(n, id)) return n;

        // 6. 店里没见过的种类：读一次原型的名字（只在店里能建原型），记下来
        if (GameApi.InStore && ItemFacts.DirectoryOf.ContainsKey(id))
        {
            var proto = ItemFacts.CreatePrototype(id);
            if (proto != null)
            {
                try
                {
                    if (IsValidKindName(proto.name, id))
                        KindNames[id] = proto.name;
                }
                catch { }
                try { proto.Destroy(); } catch { }
                if (KindNames.TryGetValue(id, out n) && IsValidKindName(n, id)) return n;
            }
        }

        // 7. 查不到时退回原始 id，绝不显示键或 Translation Error
        return id;
    }

    /// <summary>尝试获取血清或液体的本地化名称。</summary>
    public static bool TryGetSerumOrLiquidName(string id, out string name)
    {
        name = null;
        if (string.IsNullOrEmpty(id)) return false;

        // 1. 血清原型映射到液体 id
        if (SerumLiquids.TryGetValue(id, out var liqId))
        {
            string s = LiquidName(liqId);
            if (IsValidKindName(s, id) && s != liqId)
            {
                name = s;
                return true;
            }
        }

        // 2. 直接是液体 id
        string direct = LiquidName(id);
        if (IsValidKindName(direct, id) && direct != id)
        {
            name = direct;
            return true;
        }

        // 3. liquid_ 前缀
        if (id.StartsWith("liquid_"))
        {
            string trimmed = LiquidName(id[7..]);
            if (IsValidKindName(trimmed, id) && trimmed != id[7..])
            {
                name = trimmed;
                return true;
            }
        }

        // 4. 尝试在 Mechanic 表中直接查 liquid_<id>
        if (LocBridge.TryGetGameString("Mechanic", "liquid_" + id, out var mechLoc) && IsValidKindName(mechLoc, id))
        {
            name = mechLoc;
            return true;
        }

        return false;
    }

    /// <summary>验证名称是否合法，且在非中日语言下不含汉字（防止读入存档中的过时中文）。</summary>
    public static bool IsValidKindName(string name, string id)
    {
        if (!LocBridge.IsValidGameLoc(name, id)) return false;
        if (Domain.L.Locale != "zh" && Domain.L.Locale != "ja" && HasHan(name)) return false;
        return true;
    }

    public static bool HasHan(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
            if (c >= 0x4E00 && c <= 0x9FFF) return true;
        return false;
    }

    /// <summary>目录以外、游戏能读的属性名（标记、原始标签）。</summary>
    public static string Prop(string prop)
    {
        if (string.IsNullOrEmpty(prop)) return Domain.L.T("names.prop.unselected");
        if (prop.StartsWith("mark:")) return Domain.L.T("names.prop.mark", prop[5..]);
        if (prop == "sex") return Domain.L.T("prop.sex");
        // 品质：类别名 + 等级范围（等级名是游戏的）
        if (prop.StartsWith("q:") || prop.StartsWith("qt:") || prop.StartsWith("ql:") || prop.StartsWith("qok:") || prop.StartsWith("qd:")) return Quality.PropName(prop);
        // 游戏标签：游戏自己有名字的用游戏的（枪的属性），其余显示原始键名
        if (prop.StartsWith("tag:") && prop.StartsWith("tag:GUN_") && GameName.Gun(prop[4..]) is { } gun) return gun;
        if (prop.StartsWith("tag:")) return prop[4..];
        if (prop.StartsWith("str:")) return Domain.L.T("names.prop.str", prop[4..]);
        if (prop.StartsWith("has:")) return Domain.L.T("names.prop.has", prop[4..]);
        if (prop.StartsWith("pct:")) return Domain.L.T("names.prop.pct", prop[4..]);
        if (prop.StartsWith("liq:")) return Domain.L.T("names.prop.liq", LiquidName(prop[4..]));
        return prop;
    }

    /// <summary>液体的名字：从游戏本地化取当前语言的名（LocHelper.GetLocalizedMechanic("liquid_" + id)）。</summary>
    public static string LiquidName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        string key = id.StartsWith("liquid_") ? id : "liquid_" + id;
        if (LocBridge.TryGetGameString("Mechanic", key, out var loc))
            return loc;
        try
        {
            string pureId = id.StartsWith("liquid_") ? id[7..] : id;
            var l = Liquid.GetLiquidFromId(pureId);
            if (LocBridge.IsValidGameLoc(l?.displayName, id)) return l.displayName;
        }
        catch { }
        return id;
    }
}
