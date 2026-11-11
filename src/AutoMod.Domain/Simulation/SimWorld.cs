using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Rules;

namespace AutoMod.Domain.Simulation;

/// <summary>
/// 模拟世界：纯数据的物品树，实现 IWorld，供策略模拟器和单元测试使用。
/// 物品沿用 SimItem / SimCatalog；夜间变化按实测速率。
/// 从游戏导入时物品 Id 取游戏 uid，所以对象绑定的 uid 在模拟世界里同样有效。
/// </summary>
public partial class SimWorld : IWorld
{
    public readonly List<SimItem> Roots = new();
    private int _nextId = 50_000_000;
    private readonly Random _rng;

    /// <summary>夜间是否有饲料（学徒不喂，模拟器假设玩家会喂）。</summary>
    public bool Fed = true;

    /// <summary>夜里清空垃圾桶（游戏：开通并支付了清洁服务）。从游戏复制。</summary>
    public bool TrashCleared;

    public SimWorld(int seed = 1) => _rng = new Random(seed);

    public int Day { get; set; } = 1;

    // ================= 搭场景 =================

    public SimItem AddRoot(string catalogId, int? id = null, string name = null)
    {
        var it = Create(catalogId, id, name);
        Roots.Add(it);
        return it;
    }

    /// <summary>放进容器（force：不检查容量，用于导入和“添加物品”）。放不下返回 null。</summary>
    public SimItem Add(SimItem parent, string catalogId, Action<SimItem> tweak = null, bool batterySlot = false, int? id = null, bool force = false)
    {
        var it = Create(catalogId, id, null);
        tweak?.Invoke(it);
        if (batterySlot)
        {
            if (!force && (!parent.HasBatterySlot || parent.Children.Any(c => c.InBatterySlot))) return null;
            Attach(it, parent, true);
            return it;
        }
        if (!force && !FitsSim(parent, it)) return null;
        Attach(it, parent, false);
        return it;
    }

    public SimItem Create(string catalogId, int? id = null, string name = null)
    {
        var e = SimCatalog.Get(catalogId) ?? SimCatalog.Get("other");
        var it = new SimItem
        {
            Id = id ?? _nextId++, Kind = e.Kind, Name = name ?? e.Name, Capacity = e.Capacity, HasBatterySlot = e.BatterySlot,
            Accepts = e.Accepts, Num = new Dictionary<string, double>(e.Num), Str = new Dictionary<string, string>(e.Str),
        };
        if (id.HasValue && id.Value >= _nextId) _nextId = id.Value + 1;
        return it;
    }

    /// <summary>新的模拟物品 id（导入的复制品用）。</summary>
    public int NewId() => _nextId++;

    /// <summary>直接用一个已构造的物品（导入用）。</summary>
    public void Attach(SimItem item, SimItem parent, bool slot)
    {
        item.Parent?.Children.Remove(item);
        Roots.Remove(item);
        item.Parent = parent;
        item.InBatterySlot = slot;
        if (parent != null) parent.Children.Add(item);
        if (item.Id >= _nextId) _nextId = item.Id + 1;
    }

    /// <summary>没指定格子时，按物品类别猜它在游戏里会落进机器的哪个格子（游戏自己找第一个能放的格子，模拟里不知道每格收什么）。</summary>
    private static readonly (string trait, string[] roles)[] SlotHints =
    {
        ("seed", new[] { "seed" }), ("battery", new[] { "power" }), ("fish", new[] { "tank" }), ("animal", new[] { "cage" }),
        ("liquid", new[] { "water", "container", "base_water" }), ("filter", new[] { "filters" }),
    };

    private static string GuessSlot(SimItem box, SimItem item)
    {
        if (box.SlotList.Count == 0) return null;
        var traits = Catalog.Closure(TraitsOf(item));
        foreach (var (trait, roles) in SlotHints)
            if (traits.Contains(trait))
                foreach (var r in roles)
                    if (box.SlotList.Any(s => s.id == SlotNames.Prefix + r)) return SlotNames.Prefix + r;
        return null;
    }

    /// <summary>从店里拿走（消失）。</summary>
    public void Remove(SimItem item) => Detach(item);

    private void Detach(SimItem item)
    {
        item.Parent?.Children.Remove(item);
        item.Parent = null;
        Roots.Remove(item);
    }

    public IEnumerable<SimItem> All()
    {
        var stack = new Stack<SimItem>(Enumerable.Reverse(Roots));
        while (stack.Count > 0)
        {
            var x = stack.Pop();
            yield return x;
            for (int i = x.Children.Count - 1; i >= 0; i--) stack.Push(x.Children[i]);
        }
    }

    public SimItem Find(int id) => All().FirstOrDefault(x => x.Id == id);

    private static bool AcceptsSim(SimItem c, SimItem item) =>
        c.Accepts == null || c.Accepts.Count == 0 || c.Accepts.Any(a => a == item.Kind || a.StartsWith("group:") && InGroup(item, a[6..]));

    private static bool FitsSim(SimItem box, SimItem item) => box.Capacity > 0 && box.Free > 0 && AcceptsSim(box, item);

    // ================= IWorld：读取 =================

    /// <summary>全店：除了标成不算的（待售的、垃圾……）和它们里面的东西，都算。</summary>
    public IReadOnlyList<object> Shop()
    {
        var list = new List<object>();
        void Walk(SimItem it)
        {
            if (it.OffShop) return;
            list.Add(it);
            foreach (var c in it.Children) Walk(c);
        }
        foreach (var r in Roots) Walk(r);
        return list;
    }

    public IReadOnlyList<object> Named(string name) =>
        string.IsNullOrEmpty(name) ? Array.Empty<object>() : All().Where(x => x.DeviceName == name).Cast<object>().ToList();

    public string DeviceName(object item) => ((SimItem)item).DeviceName;

    public IReadOnlyList<object> Contents(object box, string slot)
    {
        var b = (SimItem)box;
        IEnumerable<SimItem> q = slot switch
        {
            "battery" => b.Children.Where(c => c.InBatterySlot),
            "filter" => b.Children.Where(c => InGroup(c, "filter")),
            SlotNames.Prefix + SlotNames.FirstTab when b.SlotList.Count == 0 => b.Children,
            not null when slot.StartsWith(SlotNames.Prefix) => b.Children.Where(c => c.InSlot == slot || SlotNames.IsTabOf(c.InSlot, slot)),
            _ => b.Children,
        };
        return q.Cast<object>().ToList();
    }

    public object Parent(object item) => ((SimItem)item).Parent;

    public bool Exists(object item)
    {
        var it = (SimItem)item;
        while (it.Parent != null) it = it.Parent;
        return Roots.Contains(it) || FixtureItems.ContainsValue(it);
    }

    public object Key(object item) => item;
    public int Uid(object item) => ((SimItem)item).Id;

    public static bool IsBox(SimItem it) => it.Capacity > 0 || it.HasBatterySlot;

    public string Kind(object item) => ((SimItem)item).Kind;

    public IReadOnlyCollection<string> Traits(object item) => TraitsOf((SimItem)item);

    /// <summary>
    /// 模拟物品的类别：从游戏导入的物品直接用游戏侧的识别结果（TraitsOverride）；
    /// 场景里搭的物品按种类和结构推断（与类别绑定表对应）。
    /// </summary>
    public static HashSet<string> TraitsOf(SimItem it)
    {
        if (it.TraitsOverride != null) return it.TraitsOverride;
        var t = new HashSet<string>();
        if (IsBox(it)) t.Add(Catalog.Container);
        switch (it.Kind)
        {
            case "animal_cage": t.Add("cage"); break;
            case "water_pitcher": t.Add("filterHolder"); break;
            case "rat": t.Add("animal"); break;
            case "raw_meat": t.Add("meat"); break;
            case "energy_credit" or "energy_credit_ext": t.Add("battery"); break;
            case "energy_credit_breeder": t.Add("selfCharging"); break;
            case "water_jug": t.Add("liquid"); break;
            case "water_filter" or "water_filter_adv": t.Add("filter"); break;
            case "kitchen_cleaver" or "combat_knife" or "hatchet": t.Add("blade"); break;
        }
        // 机器 = 游戏的商品类型 MACHINE：用电的机器，以及充能器、净水器、水分收集器
        if (it.HasBatterySlot && it.Kind is not ("storage_bay_large" or "toolbox") || it.Kind is "recharger" or "water_purifier" or "moisture_farm") t.Add("machine");
        else if (it.Capacity > 0 && t.Count == 1) t.Add("storage");
        return t;
    }

    public IReadOnlyCollection<string> KindTraits(string kind)
    {
        var x = All().FirstOrDefault(i => i.Kind == kind);
        if (x != null) return TraitsOf(x);
        var e = SimCatalog.All.FirstOrDefault(c => c.Kind == kind);
        return e == null ? Array.Empty<string>() : TraitsOf(Create(e.Id));
    }

    /// <summary>种类组（模拟器内部的容器接受规则、夜间结算用）：先看类别，再看目录。</summary>
    public static bool InGroup(SimItem it, string group) => group switch
    {
        "battery" => Catalog.Closure(TraitsOf(it)).Contains("battery"),
        "filter" => Catalog.Closure(TraitsOf(it)).Contains("filter"),
        "knife" => Catalog.Closure(TraitsOf(it)).Contains("blade"),
        "liquid" => Catalog.Closure(TraitsOf(it)).Contains("liquid"),
        _ => SimCatalog.InGroup(it.Kind, group),
    };

    public double? Num(object item, string prop)
    {
        var it = (SimItem)item;
        if (string.IsNullOrEmpty(prop)) return null;
        if (prop.StartsWith("mark:")) return it.Str.ContainsKey("mark:" + prop[5..]) ? 1 : 0;
        switch (prop)
        {
            case "uid": return it.Id;
            case "count": return IsBox(it) ? it.Used : null;
            case "free": return it.Capacity > 0 ? it.Free : null;
            case "cap": return it.Capacity > 0 ? it.Capacity : null;
            // 从游戏复制的活物（鼠、鱼）带着游戏算好的值；场景里搭的鼠按年龄推
            case "mature":
                if (it.Num.TryGetValue("adultAge", out var adultAge)) return it.N("age") >= adultAge ? 1 : 0; // 鱼：按物种的成年天数
                return it.Num.TryGetValue("mature", out var m) ? m : it.Kind == "rat" ? (it.N("age") >= 3 ? 1 : 0) : null;
            case "neutered": case "dead": return it.Num.TryGetValue(prop, out var f) ? (f > 0 ? 1 : 0) : it.Kind == "rat" ? 0 : null;
            case "freeMl": return it.Num.ContainsKey("capacityMl") ? it.N("capacityMl") - it.N("volumeMl") : null;
            case "powered": return it.HasBatterySlot ? (it.Children.Any(c => c.InBatterySlot && c.N("energy") > 0) ? 1 : 0) : null;
            case "matingCount": return it.Kind == "rat" ? it.N("matingCount") : null;
        }
        // 液体成分：模拟只记总量，水按总量算（夜里用水、接水都改总量）；其他成分用导入时的值，没有就是 0
        if (prop.StartsWith("liq:") && it.Num.ContainsKey("volumeMl"))
            return prop == "liq:water" ? it.N("volumeMl") : it.N(prop);
        if (it.Num.TryGetValue(prop, out var v)) return v;
        // 游戏标签的有无：复制时只给没有值的标记记 has:，有值的标签看它在不在（和游戏读 has: 一样）
        if (prop.StartsWith("has:")) return it.Num.ContainsKey("tag:" + prop[4..]) || it.Str.ContainsKey("str:" + prop[4..]) ? 1 : 0;
        return null;
    }

    public string Str(object item, string prop) => ((SimItem)item).Str.TryGetValue(prop, out var s) ? s : null;

    public bool Fits(object box, object item, string slot = null) => FitsSim((SimItem)box, (SimItem)item) && HasSlot((SimItem)box, slot);

    private static bool HasSlot(SimItem box, string slot) =>
        string.IsNullOrEmpty(slot) || box.SlotList.Any(s => s.id == slot) || box.SlotList.Count == 0 && slot == SlotNames.Prefix + SlotNames.FirstTab;

    public string Describe(object item)
    {
        var it = (SimItem)item;
        if (it.Kind == "rat")
        {
            string sex = Str(it, "sex") == "MALE" ? L.T("sim.sex.male") : L.T("sim.sex.female");
            string neutered = it.N("neutered") > 0 ? L.T("sim.desc.neutered") : "";
            string dead = it.N("dead") > 0 ? L.T("sim.desc.dead") : "";
            return L.T("sim.desc.rat", sex, it.Id, it.N("weight").ToString("0"), it.N("age").ToString("0"), neutered, dead);
        }
        if (InGroup(it, "battery")) return L.T("sim.desc.battery", it.Name, it.Id, (it.N("energy") * 100 / Math.Max(1, it.N("energyMax", 100))).ToString("0"));
        if (it.Num.ContainsKey("capacityMl")) return L.T("sim.desc.liquid", it.Name, it.Id, it.N("volumeMl").ToString("0"), it.N("purity").ToString("0"));
        return $"{it.Name}#{it.Id}";
    }

    // ================= IWorld：命令 =================

    private static Effect Moved(SimItem it, SimItem from, SimItem to) =>
        new() { Kind = EffectKind.Moved, Uid = it.Id, Item = it.Name, From = from?.Id ?? 0, To = to?.Id ?? 0 };

    public StepResult Place(object item, object box, string slot = null)
    {
        var it = (SimItem)item;
        var b = (SimItem)box;
        if (it.N("locked") == 1) return StepResult.Fail(L.T("sim.fail.locked", it.Name), FailCode.NotAllowed);
        if (!FitsSim(b, it)) return new StepResult { Ok = false, Reason = L.T("sim.fail.dest_full", b.Name), Code = FailCode.DestFull };
        var from = it.Parent;
        Attach(it, b, false);
        it.InSlot = slot ?? GuessSlot(b, it);
        var r = StepResult.Done(L.T("sim.step.moved_to", b.Name));
        r.Effects.Add(Moved(it, from, b));
        return r;
    }

    public StepResult Swap(object a, object b)
    {
        var t = (SimItem)a;
        var r = (SimItem)b;
        if (t.N("locked") == 1 || r.N("locked") == 1) return StepResult.Fail(L.T("sim.fail.locked", t.N("locked") == 1 ? t.Name : r.Name), FailCode.NotAllowed);
        var (tp, ts, rp, rs) = (t.Parent, t.InBatterySlot, r.Parent, r.InBatterySlot);
        var (tsl, rsl) = (t.InSlot, r.InSlot);
        Attach(r, tp, ts);
        Attach(t, rp, rs);
        (r.InSlot, t.InSlot) = (tsl, rsl);
        var res = StepResult.Done(L.T("sim.step.swapped", Describe(r)));
        res.Effects.Add(Moved(r, rp, tp));
        res.Effects.Add(Moved(t, tp, rp));
        return res;
    }

    // ================= IWorld：店里存放着的物品、作用 =================

    public IReadOnlyList<object> Stock() =>
        All().Where(x => x.Parent != null && Catalog.Matches(this, x.Parent, "storage")).OrderBy(x => x.Id).Cast<object>().ToList();

    /// <summary>
    /// 游戏是否允许工具作用于对象。从游戏复制的店铺副本由适配器填上游戏当时的回答（SimImport）；
    /// 为空 = 都允许。
    /// </summary>
    public Func<SimItem, SimItem, bool> CanApplyRule;

    /// <summary>
    /// 作用的效果。mod 不替游戏解释效果，所以默认只记一次“使用”、物品不变；
    /// 单元测试可以填上假的效果来测解释器。
    /// </summary>
    public Func<SimItem, SimItem, IReadOnlyList<object>, StepResult> ApplyRule;

    public bool CanApply(object tool, object target) =>
        Exists(target) && (DrainAllowed((SimItem)tool, (SimItem)target) ?? (CanApplyRule == null || CanApplyRule((SimItem)tool, (SimItem)target)));

    /// <summary>
    /// 容器和下水道：游戏只在容器里有液体时允许（WaterHelper.IsDrain 那条）。按模拟里的水量判断，不问游戏：
    /// 同一班先接水再倒掉时，店里真实的桶此刻还是空的。不是这种组合返回 null。
    /// </summary>
    private bool? DrainAllowed(SimItem tool, SimItem target)
    {
        if (Fixture(Fixtures.Drain) is not SimItem drain) return null;
        var other = ReferenceEquals(tool, drain) ? target : ReferenceEquals(target, drain) ? tool : null;
        if (other == null || !other.Num.ContainsKey("capacityMl")) return null;
        return other.N("volumeMl") > 0;
    }

    public StepResult Apply(object tool, object target, IReadOnlyList<object> outputs) =>
        ApplyRule?.Invoke((SimItem)tool, (SimItem)target, outputs)
        ?? Liquid((SimItem)tool, (SimItem)target)
        ?? Unknown(target, ((SimItem)tool).Kind, L.T("sim.step.unknown", ((SimItem)tool).Name, ((SimItem)target).Name));

    /// <summary>
    /// 效果未知的操作（宰杀、拆包装……）：模拟器改不了物品的状态，多天预演时每个班都会对同一件重复做。
    /// 同一件、同一种操作只算第一次，之后报“预演中已处理过”。
    /// </summary>
    private readonly HashSet<(int, string)> _unknownDone = new();

    private StepResult Unknown(object target, string how, string text) =>
        _unknownDone.Add((((SimItem)target).Id, how)) ? StepResult.Done(text)
            : StepResult.Fail(L.T("sim.fail.simulated"), FailCode.Simulated);

    /// <summary>水龙头的水质（%），实测约 90。</summary>
    public const double FaucetPurity = 90;

    /// <summary>
    /// 液体的效果照游戏的规则算（排班里最常用的几种，不算的话依赖用水的流程几天后预测就停了）：
    /// 容器和水龙头 = 接满水龙头的水；容器和下水道 = 倒空；两个液体容器 = 工具里的倒进对象，倒到满或倒空为止。
    /// 其他组合返回 null（不解释）。没变化时和游戏一样报“没有变化”，解释器会换下一件工具。
    /// </summary>
    private StepResult Liquid(SimItem tool, SimItem target)
    {
        static bool Holds(SimItem x) => x.Num.ContainsKey("capacityMl");
        SimItem faucet = Fixture(Fixtures.Faucet) as SimItem, drain = Fixture(Fixtures.Drain) as SimItem;
        SimItem other = ReferenceEquals(tool, faucet) || ReferenceEquals(tool, drain) ? target : tool;
        SimItem fixture = ReferenceEquals(other, tool) ? target : tool;
        if (fixture != null && (ReferenceEquals(fixture, faucet) || ReferenceEquals(fixture, drain)) && Holds(other))
        {
            double before = other.N("volumeMl");
            if (ReferenceEquals(fixture, faucet))
            {
                double add = other.N("capacityMl") - before;
                if (add <= 0) return StepResult.Fail(L.T("sim.fail.no_effect"), FailCode.NoEffect);
                other.Num["purity"] = (other.N("purity") * before + FaucetPurity * add) / (before + add);
                other.Num["volumeMl"] = before + add;
            }
            else
            {
                if (before <= 0) return StepResult.Fail(L.T("sim.fail.no_effect"), FailCode.NoEffect);
                other.Num["volumeMl"] = 0;
            }
            var res = StepResult.Done(L.T("sim.step.water_change", other.Name, before.ToString("0"), other.N("volumeMl").ToString("0")));
            res.Changes.Add(new StepChange(Catalog.PropName("volumeMl"), before.ToString("0"), other.N("volumeMl").ToString("0")));
            return res;
        }
        if (!Holds(tool) || !Holds(target)) return null;
        double have = tool.N("volumeMl"), had = target.N("volumeMl");
        double move = Math.Min(have, target.N("capacityMl") - had);
        if (move <= 0) return StepResult.Fail(L.T("sim.fail.no_effect"), FailCode.NoEffect);
        target.Num["purity"] = (target.N("purity") * had + tool.N("purity") * move) / (had + move);
        target.Num["volumeMl"] = had + move;
        tool.Num["volumeMl"] = have - move;
        var res2 = StepResult.Done(L.T("sim.step.water_change", target.Name, had.ToString("0"), (had + move).ToString("0")));
        res2.Changes.Add(new StepChange(Catalog.PropName("volumeMl"), had.ToString("0"), (had + move).ToString("0")));
        return res2;
    }

    /// <summary>游戏是否允许这样触发对象（how 见 Triggers）；从游戏复制的副本由适配器填上（SimImport）。为空 = 都允许（按钮要物品上有）。</summary>
    public Func<SimItem, string, bool> CanTriggerRule;

    /// <summary>触发的效果；默认同 Apply，只计 AP。</summary>
    public Func<SimItem, string, IReadOnlyList<object>, StepResult> TriggerRule;

    public IReadOnlyList<string> Buttons(object item) => ((SimItem)item).Buttons;

    public IReadOnlyList<(string id, string name)> Slots(object box) => ((SimItem)box).SlotList;

    /// <summary>固定设施（水龙头、下水道）：不在任何容器里。从游戏复制时由适配器填上。</summary>
    public readonly Dictionary<string, SimItem> FixtureItems = new();

    public object Fixture(string name) => FixtureItems.TryGetValue(name, out var f) ? f : null;

    public bool CanTrigger(object item, string how) =>
        Exists(item) && (CanTriggerRule?.Invoke((SimItem)item, how)
                         ?? (!how.StartsWith(Triggers.ButtonPrefix) || ((SimItem)item).Buttons.Contains(how[Triggers.ButtonPrefix.Length..])));

    public StepResult Trigger(object item, string how, IReadOnlyList<object> outputs) =>
        TriggerRule?.Invoke((SimItem)item, how, outputs)
        ?? (how == Triggers.Activate ? Unknown(item, how, L.T("sim.step.trigger_unknown", TriggerName(how)))
            : StepResult.Done(L.T("sim.step.trigger_unknown", TriggerName(how))));

    private static string TriggerName(string how) =>
        how == Triggers.Toggle ? L.T("sim.trigger.toggle") : how.StartsWith(Triggers.ButtonPrefix) ? L.T("sim.trigger.press", Text.ButtonName(how[Triggers.ButtonPrefix.Length..])) : L.T("sim.trigger.activate");

    public StepResult Annotate(object item, string text, bool on)
    {
        var it = (SimItem)item;
        if (on) it.Str["mark:" + text] = "1"; else it.Str.Remove("mark:" + text);
        return StepResult.Done(on ? L.T("sim.step.add_mark", text) : L.T("sim.step.remove_mark", text));
    }

    // ================= 夜间 =================

    private static bool Has(SimItem it, string trait) => it != null && Catalog.Closure(TraitsOf(it)).Contains(trait);

    /// <summary>
    /// 推进一夜：充电、耗电、产水、净化、鼠生长与繁殖。天数 +1。
    /// 模拟这几类（按类别认）：充能器、水分收集器、净水器、笼子里的鼠、鱼、水培机里的种子（返还种子的作物收获时放一颗子代）、
    /// DNA 编辑器（写入 / 提取，见 SimWorld.Dna）、带进度的机器（只推进度，不知道产物）；
    /// 数值是对游戏规则的近似（从游戏导入时带上物种的成年天数、种子的所需进度和收成等）。其他物品夜里不变。
    /// </summary>
    public void Night()
    {
        foreach (var it in All().ToList())
        {
            var p = it.Parent;
            if (InGroup(it, "battery"))
            {
                double e = it.N("energy"), max = it.N("energyMax", 100);
                if (p?.Kind == "recharger") e += 6;
                if (it.InBatterySlot && p != null) e -= p.N("drain");
                if (it.Kind == "energy_credit_breeder") e += 4;
                it.Num["energy"] = Math.Clamp(e, 0, max);
            }
            if (it.Kind == "water_filter_adv") it.Num["durability"] = Math.Min(it.N("durabilityMax", 400), it.N("durability") + 50);
            if (Has(it, "liquid") && it.Num.ContainsKey("volumeMl") && p != null)
            {
                bool powered = PowerOk(p);
                if (p.Kind == "moisture_farm" && powered)
                {
                    double v = it.N("volumeMl"), add = Math.Min(1300, it.N("capacityMl") - v);
                    if (add > 0) { it.Num["purity"] = (it.N("purity") * v + 20 * add) / (v + add); it.Num["volumeMl"] = v + add; it.Num["labelOk"] = 0; }
                }
                if (p.Kind == "water_purifier" && powered && it.N("volumeMl") > 0)
                {
                    it.Num["purity"] = Math.Min(100, it.N("purity") + 15);
                    it.Num["labelOk"] = 0;
                }
            }
        }
        foreach (var cage in All().Where(c => Has(c, "cage")).ToList()) RatsNight(cage);
        foreach (var fish in All().Where(f => Has(f, "fish") && f.N("dead") == 0).ToList()) FishNight(fish);
        foreach (var seed in All().Where(s => Has(s, "seed") && s.Parent != null && s.InSlot == SlotNames.Prefix + "seed" && !IsDnaEditor(s.Parent)).ToList()) SeedNight(seed);
        foreach (var m in All().Where(IsDnaEditor).ToList()) DnaNight(m);
        foreach (var m in All().Where(m => m.N("working") == 1 && m.N("progressMax") > 0 && !IsDnaEditor(m) && !m.Children.Any(c => Has(c, "seed"))).ToList()) MachineNight(m);
        if (TrashCleared && Fixture(Fixtures.Trash) is SimItem trash) TrashNight(trash);
        Day++;
    }

    /// <summary>垃圾桶：照游戏 PlayerStore.ClearTrash 移除里面的东西，危险废物、走私舱改装件（连同里面的）留下。</summary>
    private static void TrashNight(SimItem box)
    {
        static bool Keeps(SimItem x) => x.KeepInTrash || x.Children.Any(Keeps);
        foreach (var c in box.Children.ToList())
        {
            if (c.Kind == "smuggler_bay_mod") continue;
            if (Keeps(c)) TrashNight(c);
            else { c.Parent = null; box.Children.Remove(c); }
        }
    }

    /// <summary>鱼：每晚饿一些；低于 75% 时吃同一容器里的饲料（吃到 85%，热量从饲料扣）；吃饱（≥ 37.5%）才长一天；饿到 0 先挨饿，下一晚死。</summary>
    private void FishNight(SimItem f)
    {
        double max = f.N("maxHunger", 700), h = f.N("hunger", max);
        h -= max * 0.08;
        var feed = f.Parent?.Children.FirstOrDefault(c => c != f && !Has(c, "livestock") && c.N("calories") > 0);
        if (h < max * 0.75 && feed != null)
        {
            double want = max * 0.85 - h, eat = Math.Min(want, feed.N("calories"));
            h += eat;
            feed.Num["calories"] = feed.N("calories") - eat;
            if (feed.N("calories") <= 0) Remove(feed);
        }
        if (h <= 0)
        {
            if (f.N("starving") > 0) { f.Num["dead"] = 1; f.Num["health"] = 0; }
            f.Num["starving"] = 1;
            h = 0;
        }
        else f.Num["starving"] = 0;
        if (h >= max * 0.375) f.Num["age"] = f.N("age") + 1;
        f.Num["hunger"] = h;
    }

    /// <summary>水培：机器有电、水格的瓶子水够这一晚，进度加机器速度；够了在产出格放收成，种子用掉，机器回到空闲。</summary>
    private void SeedNight(SimItem seed)
    {
        var m = seed.Parent;
        if (!PowerOk(m)) return;
        var water = m.Children.FirstOrDefault(c => c.InSlot == SlotNames.Prefix + "water");
        double need = seed.N("waterUse");
        if (need > 0 && (water == null || water.N("volumeMl") < need)) return;
        if (water != null && need > 0) water.Num["volumeMl"] = water.N("volumeMl") - need;
        m.Num["working"] = 1;
        double progress = seed.N("growProgress") + m.N("speed", 8);
        seed.Num["growProgress"] = progress;
        double required = seed.N("growRequired", 10);
        seed.Num["growPct"] = Math.Min(100, progress * 100 / Math.Max(1, required));
        if (progress < required) return;
        string kind = seed.Str.TryGetValue("harvestKind", out var k) ? k : "other";
        for (int i = 0; i < Math.Max(1, (int)seed.N("harvestQty", 1)); i++)
        {
            var crop = new SimItem { Id = NewId(), Kind = kind, Name = Catalog.KindName(kind), InSlot = SlotNames.Prefix + "output" };
            Attach(crop, m, false);
            crop.InSlot = SlotNames.Prefix + "output";
        }
        // 返还种子的作物：子代继承特性、热度清零（游戏里各作物还可能多给一颗，这里只算种子产量倍数的整数部分加一次概率）
        if (seed.N("seedReturn") == 1)
        {
            double yield = seed.N("seedYield", 1);
            int copies = (int)yield + (_rng.NextDouble() < yield - Math.Floor(yield) ? 1 : 0);
            for (int i = 0; i < copies; i++) Attach(Offspring(seed), m, false);
        }
        Remove(seed);
        m.Num["working"] = 0;
    }

    /// <summary>别的带进度的机器（3D 打印机……）：每晚进度加速度，到了就回到空闲。产物要看配方，模拟不了，不生成。</summary>
    private static void MachineNight(SimItem m)
    {
        if (!PowerOk(m)) return;
        double p = m.N("progress") + m.N("speed", 24);
        if (p >= m.N("progressMax")) { m.Num["working"] = 0; m.Num["progress"] = 0; }
        else m.Num["progress"] = p;
    }

    private static bool PowerOk(SimItem machine)
    {
        if (!machine.HasBatterySlot) return true;
        var b = machine.Children.FirstOrDefault(c => c.InBatterySlot);
        // 电流转换器（游戏的 CURRENT_CONVERTER_TAG）：从现金买电，模拟里当作一直有电
        return b != null && (b.N("energy") > 0 || b.N("has:CURRENT_CONVERTER_TAG") == 1 || b.Kind == "current_converter");
    }

    private void RatsNight(SimItem cage)
    {
        var rats = cage.Children.Where(c => c.Kind == "rat" && c.N("dead") == 0).ToList();
        foreach (var r in rats)
        {
            if (!Fed) { r.Num["hunger"] = Math.Max(0, r.N("hunger") - 120); if (r.N("hunger") <= 0) r.Num["dead"] = 1; continue; }
            r.Num["age"] = r.N("age") + 1;
            r.Num["weight"] = r.N("weight") + 30 + 20 * r.N("gene.growth", 1);
        }
        bool male = rats.Any(r => r.Str.GetValueOrDefault("sex") == "MALE" && r.N("age") >= 3 && r.N("neutered") == 0);
        foreach (var f in rats.Where(r => r.Str.GetValueOrDefault("sex") == "FEMALE" && r.N("age") >= 3 && r.N("neutered") == 0))
        {
            if (f.N("pregnant") > 0)
            {
                f.Num["pregnant"] = 0;
                int litter = (int)Math.Round(f.N("gene.litter", 4));
                for (int i = 0; i < litter && cage.Free > 0; i++)
                {
                    var baby = Create("rat_baby");
                    baby.Str["sex"] = _rng.Next(2) == 0 ? "MALE" : "FEMALE";
                    foreach (var g in new[] { "gene.growth", "gene.litter", "gene.fertility", "gene.immunity" })
                        baby.Num[g] = Math.Max(0.1, f.N(g) * (0.85 + _rng.NextDouble() * 0.3));
                    Attach(baby, cage, false);
                }
            }
            else if (male && f.N("matingCount") < f.N("matingMax", 3))
            {
                f.Num["pregnant"] = 1;
                f.Num["matingCount"] = f.N("matingCount") + 1;
            }
        }
    }
}
