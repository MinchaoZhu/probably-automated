using System.Linq;
using AutoMod.Domain.Rules;
using System;
using System.Collections.Generic;
using AutoMod;
using AutoMod.Domain;

namespace AutoMod.Adapter;

/// <summary>
/// 属性读取。属性名有三种写法：
///   友好名（age、weight、energyPct……，见 Builtins）；
///   原始标签："tag:键" 读数字、"str:键" 读文字、"has:键" 读是否存在且启用（1/0）；
/// </summary>
/// <summary>属性值：数字或文字。不存在时两者都为空。</summary>
public readonly struct PropVal
{
    public readonly double? Num;
    public readonly string Str;

    public PropVal(double? num, string str) { Num = num; Str = str; }

    public static PropVal Of(double n) => new(n, null);
    public static PropVal Of(string s) => new(null, s);
    public static readonly PropVal None = new(null, null);

    public bool Exists => Num.HasValue || Str != null;

    /// <summary>用作分组键 / 显示。</summary>
    public string Key => Str ?? (Num.HasValue ? Num.Value.ToString("0.###") : "");

    public override string ToString() => Exists ? Key : Domain.L.T("propbindings.none");
}

public static class PropBindings
{
    /// <summary>mod 标记写在物品上的标签前缀。</summary>
    public const string MarkPrefix = "AUTOMOD_MARK_";

    private enum Kind { Number, String, Flag }

    private static readonly Dictionary<string, (Kind kind, string key)> Builtins = new()
    {
        // 鼠
        ["sex"] = (Kind.String, "ANIMAL_SEXE_TAG"),
        ["age"] = (Kind.Number, "ANIMAL_AGE_TAG"),
        ["weight"] = (Kind.Number, "ANIMAL_WEIGHT_TAG"),
        ["health"] = (Kind.Number, "ANIMAL_HEALTH_TAG"),
        ["maxHealth"] = (Kind.Number, "ANIMAL_MAX_HEALTH_TAG"),
        ["hunger"] = (Kind.Number, "ANIMAL_HUNGER_TAG"),
        ["maxHunger"] = (Kind.Number, "ANIMAL_MAX_HUNGER_TAG"),
        // 饲料机里存的热量
        ["kcal"] = (Kind.Number, "STORED_KCAL_TAG"),
        ["matingCount"] = (Kind.Number, "ANIMAL_CURRENT_MATING_COUNT_TAG"),
        ["matingMax"] = (Kind.Number, "ANIMAL_MAX_MATING_COUNT_TAG"),
        ["neutered"] = (Kind.Flag, "ANIMAL_NEUTERED_TAG"),
        ["dead"] = (Kind.Flag, "ANIMAL_IS_DEAD_TAG"),
        ["immunityBonus"] = (Kind.Number, "ANIMAL_IMMUNITY_TAG_BONUS_PERCENT"),
        ["gene.fertility"] = (Kind.Number, "ANIMAL_FERTILITY_TAG"),
        ["gene.growth"] = (Kind.Number, "ANIMAL_GROWTH_RATE_TAG"),
        ["gene.litter"] = (Kind.Number, "ANIMAL_LITTER_SIZE_TAG"),
        ["gene.longevity"] = (Kind.Number, "ANIMAL_LONGEVITY_TAG"),
        ["gene.immunity"] = (Kind.Number, "ANIMAL_IMMUNITY_TAG"),
        ["gene.hungerRate"] = (Kind.Number, "ANIMAL_HUNGER_RATE_TAG"),
        ["gene.maxHealth"] = (Kind.Number, "ANIMAL_MAX_HEALTH_TAG"),
        // 电池
        ["energy"] = (Kind.Number, "power_source_item_energy"),
        ["energyMax"] = (Kind.Number, "power_source_item_max_energy"),
        // 滤芯 / 耐久
        ["durability"] = (Kind.Number, "DURABILITY_TAG"),
        ["durabilityMax"] = (Kind.Number, "MAX_DURABILITY_TAG"),
        // 血清
        ["uses"] = (Kind.Number, "CURRENT_USE_COUNT_INT"),
    };

    public static PropVal Get(GameItem item, string prop)
    {
        if (item == null || string.IsNullOrEmpty(prop)) return PropVal.None;
        try
        {
            if (FishProps.TryGetValue(prop, out var fish) && (AquacultureHelper.IsFish(item) || AquacultureHelper.IsGroup(item))) return fish(item);
            if (prop.StartsWith("liq:")) return ReadLiquid(item, prop[4..]);
            if (Quality.Read(item, prop) is { } q) return q;
            if (prop.StartsWith("pct:")) return ReadPct(item, prop[4..]);
            if (prop.StartsWith("tag:")) return ReadTag(item, prop[4..], Kind.Number);
            if (prop.StartsWith("str:")) return ReadTag(item, prop[4..], Kind.String);
            if (prop.StartsWith("has:")) return ReadTag(item, prop[4..], Kind.Flag);
            if (prop.StartsWith("mark:")) return ReadTag(item, MarkPrefix + prop[5..], Kind.Flag);
            // 鼠的年龄要到第一夜才写标签；游戏读不到时按 0 算，这里一样
            if (prop == "age" && GameApi.Tag(item, "ANIMAL_ITEM_TAG") != null && GameApi.Tag(item, "ANIMAL_AGE_TAG") == null) return PropVal.Of(0);
            if (Builtins.TryGetValue(prop, out var b)) return ReadTag(item, b.key, b.kind);
            return Computed(item, prop);
        }
        catch (Exception ex)
        {
            Log.Warn($"[Props] 读取 {prop} 失败: {ex.Message}");
            return PropVal.None;
        }
    }

    private static PropVal Computed(GameItem item, string prop)
    {
        switch (prop)
        {
            case "kind": return PropVal.Of(item.identifier);
            case "uid": return PropVal.Of(item.uniqueId);
            case "name":
                var n = GeneralHelper.GetCustomName(item);
                return PropVal.Of(string.IsNullOrEmpty(n) ? item.name : n);
            case "mature": return PropVal.Of(HusbandryHelper.IsMature(item) ? 1 : 0);
            case "energyPct": return Pct(item, "energy", "energyMax");
            case "durabilityPct": return Pct(item, "durability", "durabilityMax");
            case "hungerPct": return Pct(item, "hunger", "maxHunger");
            case "healthPct": return Pct(item, "health", "maxHealth");
            case "calories": return ReadTag(item, "CALORIE_VALUE_TAG", Kind.Number);
            // 水培机里的种子：游戏水培的公开函数（和种子提示一致）
            case "growPct":
                if (GameApi.Tag(item, "SEED_ITEM_TAG") == null) return PropVal.None;
                int req = HydroponicHelper.GetRequiredProgress(item);
                return req <= 0 ? PropVal.None : PropVal.Of(ItemFacts.Number(GameApi.Tag(item, "GROWING_PROGRESS")) * 100.0 / req);
            case "plantHealth": return GameApi.Tag(item, "SEED_ITEM_TAG") == null ? PropVal.None : PropVal.Of(HydroponicHelper.GetHealthPercent(item));
            case "waterUse": return GameApi.Tag(item, "SEED_ITEM_TAG") == null ? PropVal.None : PropVal.Of(HydroponicHelper.GetWaterUsePerCycle(item));
            case "nutrientMin":
            case "nutrientMax":
                if (GameApi.Tag(item, "SEED_ITEM_TAG") == null) return PropVal.None;
                int lvl = HydroponicHelper.GetNutrimentLevel(item), rng = HydroponicHelper.GetNutrimentRange(item);
                return PropVal.Of(prop == "nutrientMin" ? lvl - rng : lvl + rng);
            case "repairable": return GameApi.Tag(item, "GUN_FIREPOWER_TAG") == null ? PropVal.None : PropVal.Of(GunHelper.CanRepair(item) ? 1 : 0);
            case "powered": return GameApi.BatterySlot(item) == null ? PropVal.None : PropVal.Of(SafeCanPower(item) ? 1 : 0);
            case "working":
                var st = GameApi.Tag(item, "MACHINE_STATE_TAG");
                return st == null ? PropVal.None : PropVal.Of(st.valueString == "STATE_WORKING" ? 1 : 0);
            case "progress": return ReadTag(item, "MACHINE_PROGRESS_CURRENT_TAG", Kind.Number);
            case "progressMax": return ReadTag(item, "MACHINE_PROGRESS_TARGET_TAG", Kind.Number);
            case "progressPct": return Pct(item, "progress", "progressMax");
            case "volumeMl":
                return IsLiquid(item) ? PropVal.Of(WaterHelper.GetCurrentCapacityML(item)) : PropVal.None;
            case "freeMl":
                return IsLiquid(item) ? PropVal.Of(WaterHelper.GetFreeCapacity(item) / 1000.0) : PropVal.None;
            case "purity":
                // 游戏返回万分比，转成百分比
                return IsLiquid(item) ? PropVal.Of(WaterHelper.GetWaterPurity(item) / 100.0) : PropVal.None;
            case "labelOk":
                var f = WaterFeatureHelper.GetWaterFeature(item);
                if (f == null) return PropVal.None;
                return PropVal.Of(f.realCondition?.identifier == f.fakeCondition?.identifier ? 1 : 0);
            default:
                return PropVal.None;
        }
    }

    private static bool IsLiquid(GameItem item) => GameApi.Tag(item, "LIQUID_CONTAINER_TAG") != null;

    private static PropVal Pct(GameItem item, string cur, string max)
    {
        var c = Get(item, cur);
        var m = Get(item, max);
        if (!c.Num.HasValue || !m.Num.HasValue || m.Num.Value <= 0) return PropVal.None;
        return PropVal.Of(c.Num.Value * 100.0 / m.Num.Value);
    }

    /// <summary>
    /// 鱼的活物属性：用游戏算鱼提示的公开函数（AquacultureHelper），和玩家在提示里看到的一致。
    /// 鼠的同名属性走上面的标签绑定。
    /// </summary>
    private static readonly Dictionary<string, Func<GameItem, PropVal>> FishProps = new()
    {
        ["dead"] = it => PropVal.Of(AquacultureHelper.IsDead(it) ? 1 : 0),
        ["age"] = it => PropVal.Of(it.GetTagReadonly("FISH_AGE_TAG").GetInt()),
        ["health"] = it => PropVal.Of(AquacultureHelper.GetHealth(it)),
        ["maxHealth"] = it => AquacultureHelper.GetSpecies(it) is { } s ? PropVal.Of(s.maxHealth) : PropVal.None,
        ["hunger"] = it => PropVal.Of(AquacultureHelper.GetHunger(it)),
        ["maxHunger"] = it => AquacultureHelper.GetSpecies(it) is { } s ? PropVal.Of(s.maxHunger) : PropVal.None,
        ["weight"] = it => PropVal.Of(AquacultureHelper.GetWeightGrams(it)),
        ["sex"] = it => PropVal.Of(it.GetTagReadonly("FISH_SEX_TAG").GetString() ?? ""),
        ["mature"] = it => PropVal.Of(AquacultureHelper.GetStage(it) >= FishStage.Adult ? 1 : 0),
        ["gravid"] = it => PropVal.Of(AquacultureHelper.IsGravid(it) ? 1 : 0),
    };

    /// <summary>液体成分（ml）：CURRENT_PART_液体，游戏按微升存。</summary>
    private static PropVal ReadLiquid(GameItem item, string liquid)
    {
        // 液体容器之外，粉末、药片这类也带成分标签
        var t = GameApi.Tag(item, GameTags.LiquidTag(liquid));
        if (t == null && !IsLiquid(item)) return PropVal.None;
        return PropVal.Of(t == null ? 0 : ItemFacts.Number(t) / 1000.0);
    }

    /// <summary>标签相对它的上限（同一物品上的 MAX 标签）的百分比。</summary>
    private static PropVal ReadPct(GameItem item, string key)
    {
        var keys = new HashSet<string>(ItemFacts.Tags(item.state).Select(kv => kv.Key));
        var max = GameTags.MaxOf(key, keys);
        if (max == null) return PropVal.None;
        double m = ItemFacts.Number(GameApi.Tag(item, max));
        return m <= 0 ? PropVal.None : PropVal.Of(ItemFacts.Number(GameApi.Tag(item, key)) * 100.0 / m);
    }

    /// <summary>
    /// 这些物品上出现的游戏标签，作为原始属性（"tag:键" 数字 / 开关，"str:键" 文字）：条件里可以直接比较游戏状态
    /// （机器状态、鱼的饥饿、容器里的水量……）。已经有友好名的标签不重复列。
    /// </summary>
    public static List<string> RawTagProps(IEnumerable<GameItem> items, bool includeInternal = false)
    {
        items = items.ToList();
        var named = new HashSet<string>(Builtins.Values.Select(b => b.key));
        var text = new HashSet<string>();
        var valued = new HashSet<string>();   // 见过数字或开关值的
        var liquids = new SortedSet<string>(StringComparer.Ordinal);
        var pcts = new SortedSet<string>(StringComparer.Ordinal);
        var seen = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var it in items)
        {
            var tags = ItemFacts.Tags(it.state);
            var keys = new HashSet<string>(tags.Select(kv => kv.Key));
            foreach (var kv in tags)
            {
                string k = kv.Key;
                if (named.Contains(k) || k.StartsWith(MarkPrefix)) continue;
                var t = kv.Value;
                // 液体成分单独列（ml），只列这几件里有过的
                if (GameTags.LiquidOf(k) is { } liq) { if (ItemFacts.Number(t) != 0) liquids.Add("liq:" + liq); continue; }
                if (!includeInternal && GameTags.IsInternal(k)) continue;
                seen.Add(k);
                if (!string.IsNullOrEmpty(t.valueString)) text.Add(k);
                if (ItemFacts.Number(t) != 0 || t.valueBool) valued.Add(k);
                if (GameTags.MaxOf(k, keys) != null) pcts.Add("pct:" + k);
            }
        }
        // 品质 → 成分 → 相对上限的 % → 原始标签（文字 str:；有数值 tag:；只看到“有没有”的标记再给 has:）
        var props = Quality.Props(items);
        props.AddRange(liquids);
        props.AddRange(pcts);
        foreach (var k in seen)
        {
            if (text.Contains(k)) { props.Add("str:" + k); continue; }
            props.Add("tag:" + k);
            if (!valued.Contains(k)) props.Add("has:" + k);
        }
        return props;
    }


    private static PropVal ReadTag(GameItem item, string key, Kind kind)
    {
        var t = GameApi.Tag(item, key);
        switch (kind)
        {
            case Kind.Flag:
                return PropVal.Of(t != null && t.valueEnabled ? 1 : 0);
            case Kind.String:
                return t == null ? PropVal.None : PropVal.Of(t.valueString ?? "");
            default:
                if (t == null) return PropVal.None;
                // 游戏的标签把数字存在不同字段里，取第一个非零的
                if (t.valueInt != 0) return PropVal.Of(t.valueInt);
                if (t.valueFloat != 0) return PropVal.Of(t.valueFloat);
                if (t.valueDouble != 0) return PropVal.Of(t.valueDouble);
                if (t.valueLong != 0) return PropVal.Of(t.valueLong);
                if (!string.IsNullOrEmpty(t.valueString) && double.TryParse(t.valueString, out var snum)) return PropVal.Of(snum);
                return PropVal.Of(t.valueBool ? 1 : 0); // 开关类标签存在 valueBool 里
        }
    }

    /// <summary>机器现在有电能开（电池槽里的电池够它用；游戏只用电池供电）。</summary>
    private static bool SafeCanPower(GameItem machine)
    {
        try { return MachineHelper.CanPower(machine); }
        catch { return false; }
    }
}
