using System;
using System.Collections.Generic;
using System.Linq;

using AutoMod.Domain.Rules;
using AutoMod.Domain.Simulation;
using static AutoMod.Domain.Rules.Defs;

// 测试场景：0.5 的四个排班模板（游戏里已删除），这里只当引擎测试的固定数据。

/// <summary>模板的占位对象：实例化时由向导绑定容器。</summary>
internal class TplSlot
{
    public string Key = "", Name = "";

    /// <summary>期望的类型（类别或种类），向导校验用。</summary>
    public string Expect = Catalog.Container;

    public bool Multi, Optional;
}

/// <summary>模板 = 定义包。包内 Id 是局部键，实例化时映射为全局 Id。</summary>
internal class RuleTemplate
{
    public string Id = "", Name = "", Desc = "";
    public List<TplSlot> Slots = new();
    public List<ObjectDef> Consts = new();
    public List<PropertyDef> Props = new();
    public List<SelectorDef> Selectors = new();
    public List<NoteDef> Notes = new();
    public List<WorkRule> Rules = new();

    /// <summary>规则 Id → 所属的单工位模板（旧版单工位模板，迁移时按它选取子集）。</summary>
    public Dictionary<string, string[]> Stations = new();
}

internal static class Scenarios
{
    // ================= 构造工具 =================

    private static Expr K(string constKey) => Expr.Ref(TermKind.Const, constKey);
    private static Expr S(string selKey) => Expr.Ref(TermKind.Stat, selKey);
    private static Expr V(double n) => Expr.Of(n);
    private static Cond P(string preset) => Cond.Of(preset);
    private static Cond C(string prop, string op, Expr e) => Cond.Cmp(prop, op, e);
    private static CondGroup W(params Cond[] c) => new() { Join = "all", Items = c.ToList() };

    private static SelectorDef Items(string id, string name, string[] from, string kind, CondGroup where = null,
        SortSpec sort = null, TakeSpec take = null, string minus = null, string slot = null) =>
        new() { Id = id, Name = name, Mode = SelectorMode.Items, From = from.ToList(), Kind = kind, Where = where ?? new CondGroup(), Sort = sort, Take = take, Minus = minus, Slot = slot };

    private static SelectorDef Stat(string id, string name, string[] from, string kind, string fn, string prop, CondGroup where = null) =>
        new() { Id = id, Name = name, Mode = SelectorMode.Number, From = from.ToList(), Kind = kind, Where = where ?? new CondGroup(), Agg = new AggSpec { Fn = fn, Prop = prop } };

    private static SortSpec Desc(string prop) => new() { Prop = prop, Desc = true };
    private static SortSpec Asc(string prop) => new() { Prop = prop, Desc = false };
    private static TakeSpec FromN(Expr n) => new() { Mode = TakeMode.From, N = n };
    private static TakeSpec FirstN(Expr n) => new() { Mode = TakeMode.First, N = n };
    private static Expr Plus1(string constKey) => K(constKey).Then("+", Term.N(1));

    private static ObjectDef Const(string key, string name, double v, string unit = null, string hint = null) =>
        new() { Id = key, Name = name, Value = v, Unit = unit, Hint = hint };
    private static PreCond NotEmpty(string sel) => new() { RefKind = PreRef.Selector, Ref = sel, Test = PreTest.NotEmpty };

    private static WorkRule Rule(string id, string name, PreCond[] pre, params ActionDef[] acts) =>
        new() { Id = id, Name = name, Pre = (pre ?? Array.Empty<PreCond>()).ToList(), Actions = acts.ToList() };

    private static ActionDef Move(string list, string to) => new() { Verb = ActVerb.Move, List = list, To = to };
    /// <summary>用 tool（集合）作用于 list（方向由游戏判断）。绝育 = 剪线钳，宰杀 = 刀具两次（先杀死再分解），分解 = 刀具一次。</summary>
    private static ActionDef Use(string tool, string list, int times = 1) => new() { Verb = ActVerb.Use, Tool = tool, List = list, Times = times };
    private static ActionDef Neuter(string list) => Use("cutters", list);
    private static ActionDef Slaughter(string list) => Use("knives", list, 2);
    private static ActionDef Butcher(string list) => Use("knives", list);
    private static ActionDef Swap(string list, string with) => new() { Verb = ActVerb.Swap, List = list, To = with };
    /// <summary>倒进：用来源里的液体容器作用于目标液体容器。</summary>
    private static ActionDef Pour(string sources, string targets) => Use(sources, targets);

    // ================= 四个模板 =================

    public static readonly List<RuleTemplate> All = new() { Rats(), Power(), Water(), Maintenance() };

    public static RuleTemplate Get(string id) => All.FirstOrDefault(t => t.Id == id);

    private static RuleTemplate Rats()
    {
        var t = new RuleTemplate { Id = "rats", Name = "养鼠日常", Desc = "精英笼选优 → 育种笼量产 → 育肥笼出栏，产物送肉类储藏。" };
        t.Slots.AddRange(new[]
        {
            new TplSlot { Key = "elite", Name = "精英笼", Expect = "cage" },
            new TplSlot { Key = "breeding", Name = "育种笼", Expect = "cage" },
            new TplSlot { Key = "fatten", Name = "育肥笼", Expect = "cage", Multi = true },
            new TplSlot { Key = "meat", Name = "肉类储藏", Expect = "storage" },
            new TplSlot { Key = "tools", Name = "工具箱", Expect = "storage" },
        });
        t.Consts.AddRange(new[]
        {
            Const("k_elite_male", "精英公鼠名额", 2, "只", "精英笼里保留的公鼠数量；多出来的按评分降级到育种笼或淘汰"),
            Const("k_elite_female", "精英母鼠名额", 4, "只", "精英笼里保留的母鼠数量；多出来的按评分降级到育种笼或淘汰"),
            Const("k_breed_male", "种公名额", 3, "只", "育种笼里保留的成年公鼠数量，多出来的绝育后送去育肥笼"),
            Const("k_breed_female", "种母名额", 10, "只", "育种笼里保留的成年母鼠数量，多出来的绝育后送去育肥笼"),
            Const("k_newborn", "新生天数上限", 1, "天", "育种笼里不超过这个天数的鼠算新生，绝育后转去育肥笼"),
            Const("k_weight", "出栏体重", 400, "g", "育肥笼里达到这个体重就宰杀（与出栏天数满足其一即可）"),
            Const("k_age", "出栏天数", 5, "天", "育肥笼里达到这个天数就宰杀（与出栏体重满足其一即可）"),
        });
        t.Props.Add(new PropertyDef
        {
            Id = "score", Name = "鼠评分", ItemKind = "rat",
            Formula = Expr.Of(2).Then("*", Term.Own("gene.growth")).Then("+", Term.N(1)).Then("*", Term.Own("gene.litter"))
                .Then("+", Term.N(0.02)).Then("*", Term.Own("gene.fertility")).Then("+", Term.N(0.01)).Then("*", Term.Own("gene.immunity")),
        });
        t.Selectors.AddRange(new[]
        {
            Items("cutters", "剪线钳", new[] { "tools" }, "wire_cutter"),
            Items("knives", "刀具", new[] { "tools" }, "blade"),
            Items("cage_meat", "笼中的生肉", new[] { "elite", "breeding", "fatten" }, "raw_meat"),
            Items("dead", "死亡鼠", new[] { "elite", "breeding", "fatten" }, "rat", W(P("dead"))),
            Items("ready", "待出栏鼠", new[] { "fatten" }, "rat",
                W(P("alive"), Cond.Sub("any", C("weight", ">=", K("k_weight")), C("age", ">=", K("k_age")))), Desc("weight")),
            Items("fat_unneut", "育肥笼未绝育鼠", new[] { "fatten" }, "rat", W(P("alive"), C("neutered", "=", Expr.Of(0)))),
            Items("newborn", "新生鼠", new[] { "breeding" }, "rat", W(P("alive"), C("age", "<=", K("k_newborn")))),
            Items("retired", "退役种鼠", new[] { "breeding" }, "rat",
                W(P("alive"), P("adult"), C("matingCount", ">=", Expr.Ref(TermKind.Own, "matingMax")), C("matingMax", ">", V(0)))),
            Items("breed_m", "育种公鼠", new[] { "breeding" }, "rat", W(P("alive"), P("male"), P("adult")), Desc("score")),
            Items("breed_m_extra", "超额种公", new[] { "breed_m" }, "rat", take: FromN(Plus1("k_breed_male"))),
            Items("breed_f", "育种母鼠", new[] { "breeding" }, "rat", W(P("alive"), P("female"), P("adult")), Desc("score")),
            Items("breed_f_extra", "超额种母", new[] { "breed_f" }, "rat", take: FromN(Plus1("k_breed_female"))),
            Stat("breed_free", "育种笼空位数", new[] { "breeding" }, "cage", AggFn.Sum, "free"),
            Stat("breed_m_floor", "育种公鼠底线", new[] { "breed_m" }, "rat", AggFn.Min, "score"),
            Stat("breed_f_floor", "育种母鼠底线", new[] { "breed_f" }, "rat", AggFn.Min, "score"),
            Items("elite_m", "精英公鼠", new[] { "elite" }, "rat", W(P("alive"), P("male")), Desc("score")),
            Items("elite_m_out", "落选公鼠", new[] { "elite_m" }, "rat", take: FromN(Plus1("k_elite_male"))),
            Items("elite_m_demote", "降级公鼠", new[] { "elite_m_out" }, "rat", W(C("score", ">", S("breed_m_floor"))), take: FirstN(S("breed_free"))),
            Items("elite_m_cull", "淘汰公鼠", new[] { "elite_m_out" }, "rat", minus: "elite_m_demote"),
            Items("elite_f", "精英母鼠", new[] { "elite" }, "rat", W(P("alive"), P("female")), Desc("score")),
            Items("elite_f_out", "落选母鼠", new[] { "elite_f" }, "rat", take: FromN(Plus1("k_elite_female"))),
            Items("elite_f_demote", "降级母鼠", new[] { "elite_f_out" }, "rat", W(C("score", ">", S("breed_f_floor"))), take: FirstN(S("breed_free"))),
            Items("elite_f_cull", "淘汰母鼠", new[] { "elite_f_out" }, "rat", minus: "elite_f_demote"),
        });
        t.Notes.Add(new NoteDef { Id = "killed", Name = "本班宰杀数", Init = 0 });
        t.Rules.AddRange(new[]
        {
            Rule("r_dead", "清理死亡鼠", new[] { NotEmpty("dead") }, Butcher("dead")),
            Rule("r_ready", "育肥笼出栏", new[] { NotEmpty("ready") }, Slaughter("ready"),
                new ActionDef { Verb = ActVerb.Note, Note = "killed", NoteMode = "add", Value = S("ready") }),
            Rule("r_meat", "生肉送肉类储藏", null, Move("cage_meat", "meat")),
            Rule("r_unneut", "育肥笼补充绝育", null, Neuter("fat_unneut")),
            Rule("r_newborn", "新生鼠转入育肥笼", null, Neuter("newborn"), Move("newborn", "fatten")),
            Rule("r_retired", "退役种鼠转入育肥笼", null, Neuter("retired"), Move("retired", "fatten")),
            Rule("r_extra_m", "育种笼超员 · 公", null, Neuter("breed_m_extra"), Move("breed_m_extra", "fatten")),
            Rule("r_extra_f", "育种笼超员 · 母", null, Neuter("breed_f_extra"), Move("breed_f_extra", "fatten")),
            Rule("r_elite_m", "精英笼选优 · 公", new[] { NotEmpty("elite_m_out") },
                Move("elite_m_demote", "breeding"), Neuter("elite_m_cull"), Move("elite_m_cull", "fatten")),
            Rule("r_elite_f", "精英笼选优 · 母", new[] { NotEmpty("elite_f_out") },
                Move("elite_f_demote", "breeding"), Neuter("elite_f_cull"), Move("elite_f_cull", "fatten")),
        });
        t.Stations = new()
        {
            ["r_dead"] = new[] { "fatten" }, ["r_ready"] = new[] { "fatten" }, ["r_meat"] = new[] { "fatten" }, ["r_unneut"] = new[] { "fatten" },
            ["r_newborn"] = new[] { "breeding" }, ["r_retired"] = new[] { "breeding" }, ["r_extra_m"] = new[] { "breeding" }, ["r_extra_f"] = new[] { "breeding" },
            ["r_elite_m"] = new[] { "elite" }, ["r_elite_f"] = new[] { "elite" },
        };
        return t;
    }

    private static RuleTemplate Power()
    {
        var t = new RuleTemplate { Id = "power", Name = "电力日常", Desc = "待充架 → 充能器 → 满电架 → 用电机器 → 待充架。" };
        t.Slots.AddRange(new[]
        {
            new TplSlot { Key = "bay", Name = "机器区", Expect = Catalog.Container },
            new TplSlot { Key = "full", Name = "满电架", Expect = "storage" },
            new TplSlot { Key = "pending", Name = "待充架", Expect = "storage" },
            new TplSlot { Key = "machines", Name = "维护机器组", Expect = "machine", Multi = true, Optional = true },
        });
        t.Consts.Add(Const("k_threshold", "换电阈值", 30, "%", "用电机器的电池电量低于这个值就换上满电电池"));
        t.Selectors.AddRange(new[]
        {
            Items("chargers", "充能器列表", new[] { "bay" }, "recharger"),
            Items("charged", "已充满电池", new[] { "chargers" }, "battery", W(C("energyPct", ">=", Expr.Of(95)))),
            Items("full_notfull", "满电架未充满电池", new[] { "full" }, "battery", W(C("energyPct", "<", Expr.Of(95)))),
            Items("pending_bat", "待充电池", new[] { "pending" }, "battery", W(C("energyPct", "<", Expr.Of(95))), Asc("energyPct")),
            Items("free_chargers", "空闲充能器", new[] { "chargers" }, Catalog.Self, W(P("hasRoom"))),
            Items("low_bat", "低电量电池", new[] { "machines" }, "battery", W(C("energyPct", "<", K("k_threshold"))), slot: "battery"),
            Items("full_bat", "满电电池", new[] { "full" }, "battery", sort: Desc("energyPct")),
        });
        t.Rules.AddRange(new[]
        {
            Rule("r_unload", "已充满电池转入满电架", null, Move("charged", "full")),
            Rule("r_back", "满电架未充满电池退回", null, Move("full_notfull", "pending")),
            Rule("r_load", "空闲充能器补充电池", new[] { NotEmpty("free_chargers") }, Move("pending_bat", "free_chargers")),
            Rule("r_swap", "机器更换电池", new[] { NotEmpty("low_bat") }, Swap("low_bat", "full_bat")),
        });
        t.Stations = new()
        {
            ["r_unload"] = new[] { "recharger" }, ["r_load"] = new[] { "recharger" }, ["r_back"] = new[] { "fullRack" }, ["r_swap"] = new[] { "maintained", "projector" },
        };
        return t;
    }

    private static RuleTemplate Water()
    {
        var t = new RuleTemplate { Id = "water", Name = "水务日常", Desc = "收集器的水 → 过滤水罐 → 成品罐（贴真实标签）；可选：待净化的水 → 净水器 → 成品罐。" };
        t.Slots.AddRange(new[]
        {
            new TplSlot { Key = "farms", Name = "收集器组", Expect = "moisture_farm", Multi = true },
            new TplSlot { Key = "pitcher", Name = "过滤水罐", Expect = "filterHolder" },
            new TplSlot { Key = "product", Name = "成品罐组", Expect = "liquid", Multi = true },
            new TplSlot { Key = "spares", Name = "备件箱", Expect = "storage" },
            new TplSlot { Key = "purifier", Name = "净水器组", Expect = "water_purifier", Multi = true, Optional = true },
            new TplSlot { Key = "raw", Name = "待净化区", Expect = "storage", Multi = true, Optional = true },
            new TplSlot { Key = "tools", Name = "工具箱（放标签打印机）", Expect = "storage", Optional = true },
        });
        t.Consts.Add(Const("k_purity", "达标水质", 90, "%", "净水器里的水达到这个水质就倒进成品罐"));
        t.Selectors.AddRange(new[]
        {
            Items("farm_jugs", "收集器满桶", new[] { "farms" }, "water_jug", W(C("volumeMl", ">=", V(1000)))),
            Items("worn_filter", "待换滤芯", new[] { "pitcher" }, "filter", W(C("durabilityPct", "<", V(15))), slot: "filter"),
            Items("spare_filter", "备用滤芯", new[] { "spares" }, "filter", sort: Desc("durabilityPct")),
            Items("pitcher_water", "有水的过滤水罐", new[] { "pitcher" }, Catalog.Self, W(C("volumeMl", ">", V(0)))),
            Items("product_wrong", "标签不符的成品罐", new[] { "product" }, Catalog.Self, W(C("labelOk", "=", Expr.Of(0)), C("volumeMl", ">", Expr.Of(0)))),
            Items("purified", "净水器达标桶", new[] { "purifier" }, "water_jug", W(C("purity", ">=", K("k_purity")), C("volumeMl", ">", V(0)))),
            Items("raw_jugs", "待净化桶", new[] { "raw" }, "water_jug", W(C("volumeMl", ">", V(0)), C("purity", "<", K("k_purity")))),
            Items("purifier_empty", "净水器空桶", new[] { "purifier" }, "water_jug", W(C("volumeMl", "<", Expr.Of(50)))),
            Items("product_self", "成品罐", new[] { "product" }, Catalog.Self),
            Items("labelers", "标签打印机", new[] { "tools" }, "labeler"),
            Items("pitcher_self", "过滤水罐本身", new[] { "pitcher" }, Catalog.Self),
        });
        t.Rules.AddRange(new[]
        {
            Rule("r_filter", "过滤水罐更换滤芯", new[] { NotEmpty("worn_filter") }, Swap("worn_filter", "spare_filter")),
            Rule("r_pitcher", "过滤水倒入成品罐", null, Pour("pitcher_water", "product_self")),
            Rule("r_farms", "收集水倒入过滤水罐", null, Pour("farm_jugs", "pitcher_self")),
            Rule("r_label", "成品罐贴标签", null, Use("labelers", "product_wrong")),
            Rule("r_purified", "净水器出水", null, Pour("purified", "product_self")),
            Rule("r_raw", "待净化水倒入净水器", null, Pour("raw_jugs", "purifier_empty")),
        });
        t.Stations = new()
        {
            ["r_filter"] = new[] { "filterPitcher" }, ["r_pitcher"] = new[] { "filterPitcher" }, ["r_farms"] = new[] { "moistureFarm" },
            ["r_label"] = new[] { "product" }, ["r_purified"] = new[] { "purifier" }, ["r_raw"] = new[] { "purifier" },
        };
        return t;
    }

    private static RuleTemplate Maintenance()
    {
        var t = new RuleTemplate { Id = "maintenance", Name = "机器维护", Desc = "用电机器的电池低于阈值就换上补给架里电量最高的，换下的送回收架。" };
        t.Slots.AddRange(new[]
        {
            new TplSlot { Key = "machines", Name = "维护机器组", Expect = "machine", Multi = true },
            new TplSlot { Key = "supply", Name = "补给架", Expect = "storage" },
            new TplSlot { Key = "returns", Name = "回收架", Expect = "storage" },
        });
        t.Consts.Add(Const("k_threshold", "换电阈值", 30, "%", "用电机器的电池电量低于这个值就换上满电电池"));
        t.Selectors.AddRange(new[]
        {
            Items("low_bat", "低电量电池", new[] { "machines" }, "battery", W(C("energyPct", "<", K("k_threshold"))), slot: "battery"),
            Items("supply_bat", "补给电池", new[] { "supply" }, "battery", sort: Desc("energyPct")),
        });
        t.Rules.Add(Rule("r_swap", "机器更换电池", new[] { NotEmpty("low_bat") }, Swap("low_bat", "supply_bat")));
        t.Stations = new() { ["r_swap"] = new[] { "maintained", "projector" } };
        return t;
    }

    // ================= 实例化 =================

    /// <summary>结构比较：去掉 Id、Name 后序列化比较。</summary>
    private static string Shape<T>(T x, Action<T> blank)
    {
        var c = Clone(x);
        blank(c);
        return Json(c);
    }

    /// <summary>
    /// 实例化场景（即 0.5 的模板实例化）。
    /// bindings：占位对象键 → 容器 uid；consts：常量覆盖；into：追加到已有排班（为空则新建）；
    /// rules：只实例化这些规则（为空 = 全部）。引用了未绑定的可选占位对象的规则会被省略。
    /// </summary>
    public static Schedule Instantiate(RulesData d, RuleTemplate t, Dictionary<string, List<int>> bindings,
        Dictionary<string, double> consts = null, Schedule into = null, IEnumerable<string> rules = null, string scheduleName = null, SimWorld w = null)
    {
        bindings ??= new();
        var map = new Dictionary<string, string>();
        string M(string id) => id != null && map.TryGetValue(id, out var g) ? g : id;

        // 选取规则：指定子集；去掉引用了未绑定可选占位对象的
        var unboundOptional = t.Slots.Where(s => s.Optional && (!bindings.TryGetValue(s.Key, out var b) || b.Count == 0)).Select(s => s.Key).ToHashSet();
        var tplData = new RulesData { Objects = t.Consts.ToList(), Properties = t.Props, Selectors = t.Selectors };
        // 多来源的挑选器去掉未绑定的来源（例：只有育肥笼时，“死亡鼠”只看育肥笼）
        var unbound = t.Slots.Where(s => !bindings.TryGetValue(s.Key, out var b) || b.Count == 0).Select(s => s.Key).ToHashSet();
        var tplSelectors = t.Selectors.Select(Clone).ToList();
        foreach (var ts in tplSelectors)
            if (ts.From.Count > 1 && ts.From.Any(f => !unbound.Contains(f)))
                ts.From = ts.From.Where(f => !unbound.Contains(f)).ToList();
        tplData.Selectors = tplSelectors;
        var chosen = t.Rules.Where(r => (rules == null || rules.Contains(r.Id)) && !Check.Closure(tplData, Check.RuleRefs(r)).Overlaps(unboundOptional)).ToList();
        var needed = Check.Closure(tplData, chosen.SelectMany(Check.RuleRefs));

        // 占位：给绑定的容器起设备名（已经起了名字的沿用它的名字）
        foreach (var slot in t.Slots.Where(s => needed.Contains(s.Key)))
        {
            var members = bindings.TryGetValue(slot.Key, out var b) ? b.Distinct().ToList() : new List<int>();
            var items = w == null ? new List<SimItem>() : w.All().Where(x => members.Contains(x.Id)).ToList();
            string name = items.Select(x => x.DeviceName).FirstOrDefault(n => n != null) ?? slot.Name;
            foreach (var x in items) x.DeviceName = name;
            map[slot.Key] = Places.Ref(name);
        }
        // 常量：同名同值复用
        foreach (var c in t.Consts.Where(c => needed.Contains(c.Id)))
        {
            double v = consts != null && consts.TryGetValue(c.Id, out var o) ? o : c.Value;
            var existing = d.Objects.FirstOrDefault(x => x.Name == c.Name && Math.Abs(x.Value - v) < 1e-9);
            if (existing == null)
            {
                existing = new ObjectDef { Id = NewId("o"), Name = UniqueName(d.Objects.Select(x => x.Name), c.Name), Value = v, Unit = c.Unit, Hint = c.Hint };
                d.Objects.Add(existing);
            }
            map[c.Id] = existing.Id;
        }
        // 自定义属性：结构相同复用
        foreach (var p in t.Props.Where(p => needed.Contains(p.Id)))
        {
            var np = Clone(p);
            MapExpr(np.Formula, M);
            string shape = Shape(np, x => { x.Id = ""; x.Name = ""; });
            var existing = d.Properties.FirstOrDefault(x => Shape(x, y => { y.Id = ""; y.Name = ""; }) == shape);
            if (existing == null)
            {
                np.Id = NewId("p");
                np.Name = UniqueName(d.Properties.Select(x => x.Name), p.Name);
                d.Properties.Add(np);
                existing = np;
            }
            map[p.Id] = existing.Id;
        }
        // 挑选器：按模板顺序；结构相同复用（已有的一定排在新加的上方）
        foreach (var s in tplSelectors.Where(s => needed.Contains(s.Id)))
        {
            var ns = Clone(s);
            MapSelector(ns, M);
            string shape = Shape(ns, x => { x.Id = ""; x.Name = ""; });
            var existing = d.Selectors.FirstOrDefault(x => Shape(x, y => { y.Id = ""; y.Name = ""; }) == shape);
            if (existing == null)
            {
                ns.Id = NewId("s");
                ns.Name = UniqueName(d.Selectors.Select(x => x.Name), s.Name);
                d.Selectors.Add(ns);
                existing = ns;
            }
            map[s.Id] = existing.Id;
        }
        // 排班、记事本、规则
        var sch = into;
        if (sch == null)
        {
            sch = new Schedule { Id = NewId("sch"), Name = UniqueName(d.Schedules.Select(x => x.Name), scheduleName ?? t.Name) };
            d.Schedules.Add(sch);
        }
        foreach (var n in t.Notes)
        {
            var existing = sch.Notepad.FirstOrDefault(x => x.Name == n.Name);
            if (existing == null) { existing = new NoteDef { Id = NewId("m"), Name = n.Name, Init = n.Init }; sch.Notepad.Add(existing); }
            map[n.Id] = existing.Id;
        }
        foreach (var r in chosen)
        {
            var nr = Clone(r);
            nr.Id = NewId("r");
            nr.Name = UniqueName(sch.Rules.Select(x => x.Name), r.Name);
            MapRule(nr, M);
            sch.Rules.Add(nr);
        }
        return sch;
    }

}
