using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Workforce;

namespace AutoMod.Domain.Rules;

/// <summary>类别：物品的身份或能力，一件物品可以有多个；有某类别即视为也有其全部上级类别。</summary>
public class TraitDef
{
    public string Id = "";
    public string Name => L.T("trait." + Id?.ToLowerInvariant());
    public string[] Parents = Array.Empty<string>();

    /// <summary>按物品决定技能类别时用；为空 = 不决定。</summary>
    public string Skill;
}

/// <summary>内置属性：适用类别决定界面上何时列出；Ratio 不为空时是派生属性（分子 ÷ 分母 × 100）。</summary>
public class PropDef
{
    public string Id = "";
    public string Name => L.T("prop." + Id?.ToLowerInvariant());

    /// <summary>适用类别；为空 = 任意物品。</summary>
    public string Applies;

    public (string num, string den)? Ratio;

    /// <summary>是非属性（取 0 / 1）：条件里写成“是 / 不是”。</summary>
    public bool Bool;
}

/// <summary>预设条件：属性条件的简写，和玩家的自定义条件同一语法。</summary>
public class PresetDef
{
    public string Id = "";
    public string Name => L.T("preset." + Id?.ToLowerInvariant());
    public string Applies;
    public CondGroup Where = new();
}

/// <summary>操作的机制。</summary>
public static class Mechanism
{
    public const string Place = "place";
    public const string Swap = "swap";
    public const string Apply = "apply";
    public const string Annotate = "annotate";

    /// <summary>触发：对象自己的交互（激活、开关、按机器按钮）。</summary>
    public const string Trigger = "trigger";

    /// <summary>“记录”：程序语句（写记事本），不改变世界；放在动作列表里方便界面。</summary>
    public const string Note = "note";
}

/// <summary>
/// 操作：领域里唯一改变世界的概念。能否执行、效果如何都以游戏为准；mod 只定义机制、单价和技能类别。
/// </summary>
public class OpDef
{
    public string Id = "";
    public string Name => L.T("op." + Id?.ToLowerInvariant());
    public string Mechanism = "";

    /// <summary>单价（AP）；使用按次计。</summary>
    public double Cost;

    /// <summary>固定技能类别；为空 = 按对象的类别。</summary>
    public string Skill;

    /// <summary>熟练度记到哪个操作维（Proficiency.OpDims）；为空 = 自己。</summary>
    public string Dim;
}

/// <summary>
/// 目录：类别、属性、预设、操作。规则语言、界面、模拟器、静态检查只认这里的 id。
/// 类别怎样识别不在这里，在适配器的绑定表里（游戏：GameWorld；模拟：SimWorld）。
/// </summary>
public static class Catalog
{
    // ================= 类型写法 =================

    public const string Any = "any";
    public const string Self = "self";
    public const string Container = "container";
    public const string ItemTypePrefix = "itemType:";

    /// <summary>游戏的物品家族标记（X_ITEM_TAG）自动成的类别：family:X（违禁品、化学品、器官……）。</summary>
    public const string FamilyPrefix = "family:";

    /// <summary>物品家族的显示名（游戏侧给已知家族起中文名，其余显示原始键名）。</summary>
    public static Func<string, string> FamilyName = f => f + "_ITEM_TAG";

    /// <summary>条件里的“类型”：Text 是类型（类别 / 种类 / 商品类型 / 物品家族），Op 是 = 或 !=。</summary>
    public const string TypeProp = "type";

    /// <summary>属性名由游戏侧提供（游戏自己的本地化文字）；返回 null 用目录里的名字。</summary>
    public static Func<string, string> PropNameHook = _ => null;

    /// <summary>品质属性的等级名（序号 0 最好）：适配器设置，摘要里写“至少是 基础水”而不是“≤ 2”。没有等级时为空。</summary>
    public static Func<string, IReadOnlyList<string>> GradeNames = _ => null;

    // ================= 类别 =================

    private static TraitDef T(string id, string skill = null, params string[] parents) =>
        new() { Id = id, Skill = skill, Parents = parents };

    /// <summary>顺序即显示用主类别的优先级、按物品决定技能类别的优先级。</summary>
    public static readonly List<TraitDef> Traits = new()
    {
        // 具体的物品类别排在容器前面：枪有改装槽、算容器，但显示和技能按“枪械”
        T("gun"),
        T("seed"),
        T("cage", null, Container),
        T("filterHolder", Category.Water, "liquid", Container),
        T("storage", null, Container),
        T("machine", null, Container),
        T(Container),
        T("animal", Category.Husbandry, "livestock"),
        T("fish", Category.Husbandry, "livestock"),
        T("livestock", Category.Husbandry),
        T("meat", Category.Husbandry),
        T("selfCharging", Category.Power, "battery"),
        T("battery", Category.Power),
        T("liquid", Category.Water),
        T("filter", Category.Water, "durable"),
        T("blade"),
        T("durable"),
        T("food", Category.Husbandry),
    };

    private static readonly Dictionary<string, TraitDef> TraitById = Traits.ToDictionary(t => t.Id);

    public static TraitDef Trait(string id) => id != null && TraitById.TryGetValue(id, out var t) ? t : null;

    public static bool IsTrait(string type) => type != null && (Trait(type) != null || type.StartsWith(ItemTypePrefix, StringComparison.Ordinal) || type.StartsWith(FamilyPrefix, StringComparison.Ordinal));

    /// <summary>类别 → 它自己和全部上级类别（目录里的类别是固定的，算一次）。</summary>
    private static readonly Dictionary<string, HashSet<string>> Ancestors = Traits.ToDictionary(t => t.Id, t => Closure(new[] { t.Id }));

    /// <summary>"a|b|c" 拆开的结果（筛选每件物品都要用，不每次重新拆）。</summary>
    private static readonly Dictionary<string, string[]> Alternatives = new();

    private static string[] Split(string type)
    {
        if (!Alternatives.TryGetValue(type, out var parts)) Alternatives[type] = parts = type.Split('|');
        return parts;
    }

    /// <summary>游戏商品类型的显示名（游戏侧换成游戏自己的名字）。</summary>
    public static Func<string, string> ItemTypeName = t => t;

    /// <summary>种类的中文名（游戏侧换成从物品上学到的名字）。</summary>
    public static Func<string, string> KindName = k =>
    {
        if (string.IsNullOrEmpty(k)) return "";
        if (k == "probably_automated_intercom" || k == "intercom") return L.T("item.intercom.name");
        if (k == "probably_automated_handbook" || k == "handbook") return L.T("item.handbook.name");
        if (k == "probably_automated_daily") return L.T("item.daily.name");
        if (L.Has("names.kind." + k)) return L.T("names.kind." + k);
        return L.T("kind." + k.ToLowerInvariant());
    };

    private static readonly Dictionary<string, string> SerumLiquids = new(StringComparer.OrdinalIgnoreCase)
    {
        ["serum_red"] = "livestock_steroid",
        ["serum_green"] = "livestock_immunity",
    };

    /// <summary>
    /// 纯名字解析函数：优先从表查，查不到或返回错误串时回退到原型名或 fallback，绝不返回包含 Translation Error 的字符串。
    /// </summary>
    public static string ResolveKindName(string id, Func<string, string> tableLookup, string prototypeName, string fallback = null, Func<string, string> mechanicLookup = null)
    {
        if (string.IsNullOrEmpty(id)) return fallback ?? "";

        // 1. mod 物品的显示名永远按当前语言取表
        if (id == "probably_automated_intercom" || id == "intercom") return L.T("item.intercom.name");
        if (id == "probably_automated_handbook" || id == "handbook") return L.T("item.handbook.name");
        if (id == "probably_automated_daily") return L.T("item.daily.name");

        // 2. 查 Item 表（item_<id>_name、husbandry_<id>_name、<id>_name）
        if (tableLookup != null)
        {
            try
            {
                string key = "item_" + id + "_name";
                string loc = tableLookup(key);
                if (IsValidName(loc, key)) return loc;
            }
            catch { }
            try
            {
                string husKey = "husbandry_" + id + "_name";
                string loc = tableLookup(husKey);
                if (IsValidName(loc, husKey)) return loc;
            }
            catch { }
            try
            {
                string dirKey = id + "_name";
                string loc = tableLookup(dirKey);
                if (IsValidName(loc, dirKey)) return loc;
            }
            catch { }
        }

        // 3. 查液体 / Mechanic 表（血清映射到液体，或直接查 liquid_<id>）
        var mLookup = mechanicLookup ?? tableLookup;
        if (mLookup != null)
        {
            try
            {
                if (SerumLiquids.TryGetValue(id, out var liqId))
                {
                    string liqKey = "liquid_" + liqId;
                    string loc = mLookup(liqKey);
                    if (IsValidName(loc, liqKey)) return loc;
                }
                string directKey = id.StartsWith("liquid_") ? id : "liquid_" + id;
                string dLoc = mLookup(directKey);
                if (IsValidName(dLoc, directKey)) return dLoc;
                if (id.StartsWith("liquid_"))
                {
                    string trimmed = "liquid_" + id[7..];
                    string tLoc = mLookup(trimmed);
                    if (IsValidName(tLoc, trimmed)) return tLoc;
                }
            }
            catch { }
        }

        // 4. mod 本地化表
        if (L.Has("names.kind." + id)) return L.T("names.kind." + id);
        if (L.Has("kind." + id))
        {
            string t = L.T("kind." + id);
            if (IsValidName(t, "kind." + id) && t != id) return t;
        }

        // 5. 原型名（非 zh 且非 ja 模式下不得含汉字）
        if (IsValidName(prototypeName, id) && (L.Locale == "zh" || L.Locale == "ja" || !HasHan(prototypeName)))
            return prototypeName;

        // 6. 回退名（非 zh 且非 ja 模式下不得含汉字）
        if (fallback != null && IsValidName(fallback, id) && (L.Locale == "zh" || L.Locale == "ja" || !HasHan(fallback)))
            return fallback;

        return fallback ?? id;
    }

    public static bool HasHan(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
            if (c >= 0x4E00 && c <= 0x9FFF) return true;
        return false;
    }

    /// <summary>判断解析出的名字是否有效（非空、不等于键名、不包含 Translation Error 或 No translation found）。</summary>
    public static bool IsValidName(string name, string key)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name == key) return false;
        if (name.StartsWith("Translation Error", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.StartsWith("No translation found", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>类别集合加上全部上级类别。</summary>
    public static HashSet<string> Closure(IEnumerable<string> traits)
    {
        var set = new HashSet<string>();
        var stack = new Stack<string>(traits ?? Enumerable.Empty<string>());
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            if (!set.Add(t)) continue;
            foreach (var p in Trait(t)?.Parents ?? Array.Empty<string>()) stack.Push(p);
        }
        return set;
    }

    /// <summary>物品有这个类别：直接有，或它的某个类别的上级是它。筛选时每件物品都要问，不建集合。</summary>
    public static bool HasTrait(IWorld w, object item, string trait)
    {
        foreach (var t in w.Traits(item))
            if (t == trait || (Ancestors.TryGetValue(t, out var up) && up.Contains(trait))) return true;
        return false;
    }

    /// <summary>类型匹配：任意 / 种类 / 类别（含下级）；支持 | 并列。“容器本身”在展开时处理。</summary>
    public static bool Matches(IWorld w, object item, string type)
    {
        if (string.IsNullOrEmpty(type) || type == Any) return true;
        if (type.IndexOf('|') >= 0)
        {
            foreach (var t in Split(type))
                if (Matches(w, item, t)) return true;
            return false;
        }
        if (type == Self) return true;
        return IsTrait(type) ? HasTrait(w, item, type) : w.Kind(item) == type;
    }

    /// <summary>这个类型写法静态可知的类别（种类靠种类档案；不知道时为空）。</summary>
    public static HashSet<string> TypeTraits(string type, IWorld w = null)
    {
        if (string.IsNullOrEmpty(type) || type == Any) return new HashSet<string>();
        if (type == Self) return new HashSet<string> { Container };
        if (type.Contains('|'))
        {
            var parts = type.Split('|').Select(t => TypeTraits(t, w)).ToList();
            var common = new HashSet<string>(parts[0]);
            foreach (var p in parts.Skip(1)) common.IntersectWith(p);
            return common;
        }
        if (IsTrait(type)) return Closure(new[] { type });
        return Closure(w?.KindTraits(type) ?? Array.Empty<string>());
    }

    /// <summary>结果是容器（可作来源 / 目标 / 工具来源）的类型写法。</summary>
    public static bool IsContainerType(string type) => type == Self || (IsTrait(type) && TypeTraits(type).Contains(Container));

    /// <summary>
    /// 列表类型是否满足要求（操作的对象要求、倒进的目标）。种类没有档案时不报错（能否执行仍以游戏为准）。
    /// </summary>
    public static bool Satisfies(string listType, string required, IWorld w = null)
    {
        if (string.IsNullOrEmpty(required) || string.IsNullOrEmpty(listType) || listType == Any) return true;
        if (listType.Contains('|')) return listType.Split('|').All(t => Satisfies(t, required, w));
        if (listType == required) return true;
        if (listType == Self) return true;
        if (!IsTrait(listType) && !IsTrait(required)) return false; // 两个不同的种类
        if (!IsTrait(listType) && (w?.KindTraits(listType) is not { Count: > 0 })) return true;
        return TypeTraits(listType, w).Contains(required);
    }

    public static string TypeName(string type)
    {
        if (string.IsNullOrEmpty(type)) return L.T("text.unset");
        if (type.Contains('|')) return L.Join("list", type.Split('|').Select(TypeName));
        if (type == Any) return L.T("kind.any");
        if (type == Self) return L.T("kind.self");
        if (type.StartsWith(ItemTypePrefix)) return L.T("kind.any_type", ItemTypeName(type[ItemTypePrefix.Length..]));
        if (type.StartsWith(FamilyPrefix)) return L.T("kind.any_family", FamilyName(type[FamilyPrefix.Length..]));
        return Trait(type)?.Name ?? KindName(type);
    }

    /// <summary>显示用主类别：按目录顺序第一个命中的。</summary>
    public static string PrimaryTrait(IWorld w, object item)
    {
        var all = Closure(w.Traits(item));
        return Traits.FirstOrDefault(t => all.Contains(t.Id))?.Id;
    }

    /// <summary>按物品决定技能类别：目录顺序第一个声明了技能类别的类别；没有则为杂务。</summary>
    public static string SkillOf(IWorld w, object item)
    {
        var all = Closure(w.Traits(item));
        return Traits.FirstOrDefault(t => t.Skill != null && all.Contains(t.Id))?.Skill ?? Category.Misc;
    }

    // ================= 属性 =================

    private static PropDef P(string id, string applies, (string, string)? ratio = null) =>
        new() { Id = id, Applies = applies, Ratio = ratio };

    public static readonly List<PropDef> Props = new()
    {
        // 活物（鼠、鱼……）共用；值由游戏侧按物种读（鱼用游戏算提示的函数）
        P("weight", "livestock"), P("age", "livestock"), P("health", "livestock"),
        P("healthPct", "livestock", ("health", "maxHealth")),
        P("hungerPct", "livestock", ("hunger", "maxHunger")),
        P("mature", "livestock"), P("dead", "livestock"),
        // 只有鼠类
        P("matingCount", "animal"), P("matingMax", "animal"), P("neutered", "animal"),
        // 只有鱼
        P("gravid", "fish"),
        P("gene.growth", "animal"), P("gene.litter", "animal"), P("gene.fertility", "animal"),
        P("gene.immunity", "animal"), P("gene.longevity", "animal"),
        // 机器：游戏的机器状态（MACHINE_STATE_TAG）是“工作中”
        P("working", "machine"), P("progressPct", "machine", ("progress", "progressMax")),
        P("energy", "battery"), P("energyPct", "battery", ("energy", "energyMax")), P("energyMax", "battery"),
        P("durability", "durable"), P("durabilityPct", "durable", ("durability", "durabilityMax")),
        P("calories", "food"),
        // 水培机里的种子（游戏水培的公开函数）
        P("growPct", "seed"), P("plantHealth", "seed"), P("waterUse", "seed"),
        P("nutrientMin", "seed"), P("nutrientMax", "seed"),
        P("repairable", "gun"),
        P("volumeMl", "liquid"), P("freeMl", "liquid"), P("purity", "liquid"), P("labelOk", "liquid"),
        P("count", Container), P("free", Container), P("cap", Container),
        // 位置（Eval 按所在的容器算）：零设置的示例靠它们分开“储藏区里闲着的”和“机器里用着的”
        P("powered", "machine"), P("inStorage", null), P("inBatterySlot", "battery"),
    };

    public static PropDef Prop(string id) => Props.FirstOrDefault(p => p.Id == id);

    private static readonly HashSet<string> BoolProps = new() { "mature", "dead", "neutered", "gravid", "working", "repairable", "labelOk", "powered", "inStorage", "inBatterySlot" };

    /// <summary>是非属性的否定名称（支持本地化语义键 prop.<id>.not）。</summary>
    public static string PropNotName(string id)
    {
        if (id == null || !IsBool(id)) return null;
        if (id.StartsWith("has:")) return L.T("names.prop.has_no", TagNames.Name(id[4..]) ?? id[4..]);
        return L.T("prop." + id.ToLowerInvariant() + ".not");
    }

    /// <summary>是非属性的肯定名称：游戏标签的有无写成“有 X”（和“没有 X”成对）；其余用属性名（返回 null）。</summary>
    public static string PropYesName(string id) =>
        id != null && id.StartsWith("has:") ? L.T("names.prop.has_yes", TagNames.Name(id[4..]) ?? id[4..]) : null;

    public static Func<string, string> GradeShortNameHook = _ => null;

    /// <summary>等级属性的短名（水质等级 → 水质）。</summary>
    public static string GradeShortName(string id)
    {
        if (id == null) return null;
        if (GradeShortNameHook(id) is { } hooked) return hooked;
        string key = "prop." + id.ToLowerInvariant() + ".grade_short";
        return L.Has(key) ? L.T(key) : null;
    }

    /// <summary>是非属性（内置的几个，加上游戏标签里只有有无的 has:）。</summary>
    public static bool IsBool(string id) => id != null && (BoolProps.Contains(id) || id.StartsWith("has:"));
    public static string PropName(string id) => Prop(id) == null ? null : PropNameHook(id) ?? Prop(id).Name;

    /// <summary>某类型可用的内置属性：全部物品 → 非容器属性；容器本身 → 容器与液体属性；其余按类别。</summary>
    public static IEnumerable<string> PropsFor(string type, IWorld w = null)
    {
        if (string.IsNullOrEmpty(type) || type == Any) return Props.Where(p => p.Applies != Container).Select(p => p.Id);
        var traits = TypeTraits(type, w);
        if (type == Self) traits.Add("liquid");
        if (traits.Count == 0) return Props.Where(p => p.Applies != Container).Select(p => p.Id);
        return Props.Where(p => p.Applies == null || traits.Contains(p.Applies)).Select(p => p.Id);
    }

    // ================= 预设条件 =================

    private static Cond C(string prop, string op, double v) => Cond.Cmp(prop, op, Expr.Of(v));
    private static Cond C(string prop, string op, Expr e) => Cond.Cmp(prop, op, e);

    private static PresetDef S(string id, string applies, params Cond[] all) =>
        new() { Id = id, Applies = applies, Where = new CondGroup { Items = all.ToList() } };

    /// <summary>
    /// 常用条件：只放示例用到的、直接对应游戏标记的几个（不自定数字门槛，数字由玩家在条件里写）。
    /// </summary>
    public static readonly List<PresetDef> Presets = new()
    {
        S("alive", "livestock", C("dead", "=", 0)),
        S("dead", "livestock", C("dead", "=", 1)),
        S("male", "livestock", new Cond { Prop = "sex", Op = "=", Text = "MALE" }),
        S("female", "livestock", new Cond { Prop = "sex", Op = "=", Text = "FEMALE" }),
        S("adult", "livestock", C("mature", "=", 1)),
        S("hasRoom", Container, C("free", ">", 0)),
    };

    public static PresetDef Preset(string id) => Presets.FirstOrDefault(p => p.Id == id);

    /// <summary>某类型可用的预设条件。</summary>
    public static IEnumerable<PresetDef> PresetsFor(string type, IWorld w = null)
    {
        if (string.IsNullOrEmpty(type) || type == Any) return Presets.Where(p => p.Applies != Container);
        var traits = TypeTraits(type, w);
        if (traits.Count == 0) return Presets.Where(p => p.Applies != Container);
        return Presets.Where(p => p.Applies == null || traits.Contains(p.Applies));
    }

    // ================= 操作 =================

    public static readonly List<OpDef> Ops = new()
    {
        // 单价都是整数 AP（每件实际花费再乘熟练度系数）
        new() { Id = ActVerb.Move, Mechanism = Mechanism.Place, Cost = 1 },
        new() { Id = ActVerb.Swap, Mechanism = Mechanism.Swap, Cost = 4 },
        new() { Id = ActVerb.Use, Mechanism = Mechanism.Apply, Cost = 3 },
        new() { Id = ActVerb.Activate, Mechanism = Mechanism.Trigger, Cost = 2, Dim = ActVerb.Use },
        new() { Id = ActVerb.Toggle, Mechanism = Mechanism.Trigger, Cost = 1, Dim = ActVerb.Use },
        new() { Id = ActVerb.Press, Mechanism = Mechanism.Trigger, Cost = 2, Dim = ActVerb.Use },
        new() { Id = ActVerb.Mark, Mechanism = Mechanism.Annotate },
        new() { Id = ActVerb.Unmark, Mechanism = Mechanism.Annotate },
        new() { Id = ActVerb.Note, Mechanism = Mechanism.Note },
    };

    public static OpDef Op(string id) => Ops.FirstOrDefault(o => o.Id == id) ?? Ops[0];
}
