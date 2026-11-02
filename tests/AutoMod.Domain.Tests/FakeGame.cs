using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Simulation;

/// <summary>
/// 测试用的假游戏规则：给模拟世界填上“能否作用”和“作用效果”（真正的模拟世界不知道效果，见 SimWorld.ApplyRule），
/// 用来测解释器的“使用”：找工具、次数、产物去向、没有效果、需要玩家操作。数值仿照游戏。
/// - 剪线钳 → 活的、成年、未绝育的鼠：绝育
/// - 刀具 → 活鼠：杀死；死鼠：分解成生肉（每 150g 一块，放进产物去向，放不下留在原笼）
/// - 液体容器 → 液体容器：把水倒过去（目标满了 / 来源空了：没有效果）
/// - 标签打印机 → 液体容器：允许，但没有效果（游戏只按当前标签重印）
/// - 杂物（模拟目录里的 other，扮演放大镜）→ 任何东西：要打开界面让玩家看（需要玩家操作）
/// - 激活：电池（扮演刮刮乐）用掉，产出一件杂物；杂物（扮演文件）要打开界面
/// - 开关：充能器切换 on（0 ↔ 1）
/// - 液体容器 → 水龙头：接满水
/// </summary>
internal static class FakeGame
{
    /// <summary>
    /// 模拟游戏环境下的物品名解析（测试用）：模拟游戏本地化表缺失某些物品键（如血清、植物DNA芯片），此时绝不能返回 Translation Error。
    /// </summary>
    public static string SimulateGameItemName(string id, string protoName = null, string staleSaveName = null)
    {
        // 模拟 Unity Localization 在键缺失时返回的错误字符串，以及游戏表内条目
        string ItemLookup(string key)
        {
            if (key == "item_serum_red_name") return "Translation Error 'item_serum_red_name' in Item";
            if (key == "item_serum_green_name") return "Translation Error 'item_serum_green_name' in Item";
            if (key == "item_plant_dna_disk_name") return "No translation found for 'item_plant_dna_disk_name' in 'Item'";
            if (key == "husbandry_rat_name") return AutoMod.Domain.L.Locale == "zh" ? "老鼠" : "Rat";
            if (key == "item_water_filter_name") return AutoMod.Domain.L.Locale == "zh" ? "滤芯" : "Water Filter";
            if (key == "item_storage_bay_large_name") return AutoMod.Domain.L.Locale == "zh" ? "储藏区（扩建）" : "Storage (Expansion)";
            if (key == "item_storage_name") return AutoMod.Domain.L.Locale == "zh" ? "储藏区" : "Storage";
            if (key == "item_toolbox_name") return AutoMod.Domain.L.Locale == "zh" ? "工具箱" : "Toolbox";
            if (key == "item_labeler_blue_name") return AutoMod.Domain.L.Locale == "zh" ? "蓝色标签打印机" : "Blue Labeler";
            return key; // 游戏默认返回键名
        }

        string MechanicLookup(string key)
        {
            if (key == "liquid_livestock_steroid") return AutoMod.Domain.L.Locale == "zh" ? "牲畜类固醇血清" : "Livestock Steroid Serum";
            if (key == "liquid_livestock_immunity") return AutoMod.Domain.L.Locale == "zh" ? "牲畜免疫血清" : "Livestock Immunity Serum";
            if (key == "liquid_water") return AutoMod.Domain.L.Locale == "zh" ? "水" : "Water";
            if (key == "liquid_acid") return AutoMod.Domain.L.Locale == "zh" ? "酸" : "Acid";
            return key;
        }

        return Catalog.ResolveKindName(id, ItemLookup, protoName, staleSaveName, MechanicLookup);
    }

    public static T Install<T>(T w) where T : SimWorld
    {
        w.CanApplyRule = (tool, t) => CanApply(w, tool, t);
        w.ApplyRule = (tool, t, outputs) => Apply(w, tool, t, outputs);
        w.CanTriggerRule = (it, how) => how == Triggers.Toggle ? it.Kind == "recharger" : how == Triggers.Activate ? Is(w, it, "battery") || it.Kind == "other" : it.Buttons.Contains(how[Triggers.ButtonPrefix.Length..]);
        w.TriggerRule = (it, how, outputs) => Trigger(w, it, how, outputs);
        return w;
    }

    private static bool Is(SimWorld w, SimItem it, string type) => Catalog.Matches(w, it, type);

    private static bool CanApply(SimWorld w, SimItem tool, SimItem t)
    {
        if (t.Kind == "faucet") return Is(w, tool, "liquid") && tool.Num.ContainsKey("capacityMl");
        if (tool.Kind == "wire_cutter") return Is(w, t, "animal") && (w.Num(t, "dead") ?? 0) == 0 && (w.Num(t, "mature") ?? 0) == 1 && (w.Num(t, "neutered") ?? 0) == 0;
        if (Is(w, tool, "blade")) return Is(w, t, "animal");
        if (Is(w, tool, "liquid")) return Is(w, t, "liquid") && t.Num.ContainsKey("capacityMl");
        if (tool.Kind == "labeler") return Is(w, t, "liquid");
        if (tool.Kind == "other") return true;
        return false;
    }

    private static StepResult Apply(SimWorld w, SimItem tool, SimItem t, IReadOnlyList<object> outputs)
    {
        if (tool.Kind == "other") return StepResult.Fail(AutoMod.Domain.L.IsZh ? "要打开检视界面" : "Inspection window required", FailCode.NeedsPlayer);
        if (t.Kind == "faucet")
        {
            if (tool.N("volumeMl") >= tool.N("capacityMl")) return StepResult.Fail(AutoMod.Domain.L.IsZh ? "没有变化" : "No change", FailCode.NoEffect);
            return Set(tool, "volumeMl", tool.N("capacityMl"), AutoMod.Domain.L.IsZh ? "接满水" : "Filled with water");
        }
        if (tool.Kind == "labeler") return StepResult.Fail(AutoMod.Domain.L.IsZh ? "没有变化" : "No change", FailCode.NoEffect);
        if (tool.Kind == "wire_cutter") return Set(t, "neutered", 1, AutoMod.Domain.L.IsZh ? "绝育" : "Neutered");
        if (Is(w, tool, "blade")) return t.N("dead") == 0 ? Set(t, "dead", 1, AutoMod.Domain.L.IsZh ? "杀死" : "Killed") : Butcher(w, t, outputs);
        return Pour(w, tool, t);
    }

    private static StepResult Trigger(SimWorld w, SimItem it, string how, IReadOnlyList<object> outputs)
    {
        if (how == Triggers.Toggle) return Set(it, "on", 1 - it.N("on"), AutoMod.Domain.L.IsZh ? "切换" : "Toggled");
        if (how.StartsWith(Triggers.ButtonPrefix)) return Set(it, "pressed", it.N("pressed") + 1, AutoMod.Domain.L.IsZh ? "按下" : "Pressed");
        if (it.Kind == "other") return StepResult.Fail(AutoMod.Domain.L.IsZh ? "要打开文件" : "Document window required", FailCode.NeedsPlayer);
        var parent = it.Parent;
        w.Remove(it);
        var prize = w.Create("other");
        var dest = outputs?.Cast<SimItem>().FirstOrDefault(b => w.Fits(b, prize)) ?? parent;
        if (dest != null) w.Attach(prize, dest, false);
        var res = StepResult.Done(AutoMod.Domain.L.IsZh ? "用掉，产出杂物" : "Used, produced junk");
        res.Effects.Add(new Effect { Kind = EffectKind.Removed, Uid = it.Id, Item = it.Name, From = parent?.Id ?? 0 });
        res.Effects.Add(new Effect { Kind = EffectKind.Created, Uid = prize.Id, Item = prize.Name, To = prize.Parent?.Id ?? 0 });
        return res;
    }

    private static StepResult Set(SimItem t, string prop, double v, string text)
    {
        var res = StepResult.Done(text);
        res.Effects.Add(new Effect { Kind = EffectKind.Changed, Uid = t.Id, Item = t.Name, Prop = prop, Old = t.N(prop), New = v });
        t.Num[prop] = v;
        return res;
    }

    private static StepResult Butcher(SimWorld w, SimItem t, IReadOnlyList<object> outputs)
    {
        var cage = t.Parent;
        int pieces = Math.Max(1, (int)(t.N("weight") / 150));
        w.Remove(t);
        var res = StepResult.Done(AutoMod.Domain.L.IsZh ? $"分解，生肉 ×{pieces}" : $"Butchered, raw meat x{pieces}");
        res.Effects.Add(new Effect { Kind = EffectKind.Removed, Uid = t.Id, Item = t.Name, From = cage?.Id ?? 0 });
        for (int k = 0; k < pieces; k++)
        {
            var meat = w.Create("meat");
            var dest = outputs?.Cast<SimItem>().FirstOrDefault(b => w.Fits(b, meat));
            if (dest != null) w.Attach(meat, dest, false);
            else if (cage != null && w.Fits(cage, meat)) w.Attach(meat, cage, false);
            res.Effects.Add(new Effect { Kind = EffectKind.Created, Uid = meat.Id, Item = meat.Name, To = meat.Parent?.Id ?? 0 });
        }
        return res;
    }

    private static StepResult Pour(SimWorld w, SimItem s, SimItem dest)
    {
        double before = s.N("volumeMl");
        double amount = Math.Min(before, dest.N("capacityMl") - dest.N("volumeMl"));
        if (amount <= 0) return StepResult.Fail(AutoMod.Domain.L.IsZh ? "没有变化" : "No change", FailCode.NoEffect);
        double purity = s.N("purity");
        var filter = dest.Children.FirstOrDefault(c => SimWorld.InGroup(c, "filter") && c.N("durability") > 0);
        if (filter != null) { purity = Math.Max(purity, 96); filter.Num["durability"] = Math.Max(0, filter.N("durability") - amount / 1000 * 14); }
        double dv = dest.N("volumeMl");
        dest.Num["purity"] = dv + amount <= 0 ? purity : (dest.N("purity") * dv + purity * amount) / (dv + amount);
        dest.Num["volumeMl"] = dv + amount;
        dest.Num["labelOk"] = 0;
        s.Num["volumeMl"] = before - amount;
        var res = StepResult.Done(AutoMod.Domain.L.IsZh ? $"倒入 {amount:0}ml" : $"Poured {amount:0}ml");
        res.Effects.Add(new Effect { Kind = EffectKind.Changed, Uid = s.Id, Item = s.Name, Prop = "volumeMl", Old = before, New = before - amount });
        res.Effects.Add(new Effect { Kind = EffectKind.Changed, Uid = dest.Id, Item = dest.Name, Prop = "volumeMl", Old = dv, New = dv + amount });
        return res;
    }
}
