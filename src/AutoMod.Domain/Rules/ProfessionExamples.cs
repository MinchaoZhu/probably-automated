using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Rules;

/// <summary>
/// 各职业的示例排班：不用设置。机器从全店按种类找（装好了的）；原料从全店闲着的东西里拿（不在机器里的）；
/// 产物放到叫“××仓库”的设备，店里没有这个设备名或者满了，就放任意有空位的储藏区。
/// 玩家想指定放哪里，就在店里给储藏区起这个设备名。数值（阈值）是起点，照自己的店调。
/// </summary>
public static class ProfessionExamples
{
    public sealed class Spec
    {
        public readonly string Id;
        public readonly string[] MachineKinds;

        public Spec(string id, string[] machineKinds)
        {
            Id = id; MachineKinds = machineKinds;
        }

        public string Name => L.T($"example.spec.{Id}.name");
        public string Note => L.T($"example.spec.{Id}.note");
    }

    /// <summary>可建的示例：id、用到的机器种类（图标用第一个）。顺序即“新建流程”里的顺序（整理紧跟在自动换电后面）。</summary>
    public static readonly Spec[] All =
    {
        new("tidy", new[] { "storage_bay_large" }),
        new("hydro", new[] { "hydroponic" }),
        new("aqua", new[] { "ras" }),
        new("ranch", new[] { "animal_cage" }),
        new("gun", new[] { "printer" }),
        new("water", new[] { "bottle_printer", "water_purifier", "vending_fountain" }),
        new("dna_disk", new[] { "plant_dna_manipulator" }),
        new("dna_edit", new[] { "plant_dna_manipulator" }),
        new("dna_breed", new[] { "plant_dna_manipulator", "hydroponic" }),
    };

    private static string T(string key) => L.T(key);

    private sealed class B
    {
        public readonly RulesData D;
        public readonly Schedule S;

        public B(RulesData d, string name)
        {
            D = d;
            S = new Schedule { Id = Defs.NewId("sch"), Created = Defs.Now(), Name = Defs.UniqueName(d.Schedules.Select(x => x.Name), name) };
            d.Schedules.Add(S);
        }

        private string Unique(bool selector, string name)
        {
            string n = name;
            for (int i = 2; Scope.NameConflict(D, selector, n, S.Id) != null; i++) n = L.T("transfer.dup_suffix", name, i);
            return n;
        }

        /// <summary>全店的这种机器（不看有没有电：没电时收获、取成品这类不用电的任务照样做）。</summary>
        public string Machines(string name, string kind) => Sel(name, Places.Shop, kind);

        /// <summary>全店有电的这种机器：要往里放料、按按钮的（没电的备用机放了料也不运转）。</summary>
        public string Powered(string name, string kind) =>
            Sel(name, Places.Shop, kind, where: Cond.Cmp("powered", "=", Expr.Of(1)));

        /// <summary>全店闲着的这种东西（直接放在储藏区里，不在机器、充能器、笼子里）。</summary>
        public string Idle(string name, string kind, int take = 0, params Cond[] where) =>
            Sel(name, Places.Shop, kind, take: take, where: new[] { Cond.Cmp("inStorage", "=", Expr.Of(1)) }.Concat(where).ToArray());

        private readonly Dictionary<string, string> _stores = new();
        private string _anyStorage;

        /// <summary>全店有空位的储藏区。</summary>
        private string AnyStorage => _anyStorage ??= Sel(T("example.sel.any_storage"), Places.Shop, "storage", where: Cond.Of("hasRoom"));

        /// <summary>
        /// 放入叫 name 的储藏区；店里没有这个设备名或它满了，就放入任意有空位的储藏区。
        /// 目标是一个有序集合：来源先【name】后全店，放入时按顺序找第一个放得下的。
        /// </summary>
        public ActionDef ToStore(string list, string name)
        {
            if (!_stores.TryGetValue(name, out var to))
            {
                // 范围是“本身”：先是叫这个名字的储藏区，然后是全店有空位的储藏区
                string selName = L.T("example.sel.store_or_any", name);
                to = Sel(selName, Places.Ref(name), Catalog.Self, where: Cond.Of("hasRoom"));
                D.Selectors.First(x => x.Id == to).From.Add(AnyStorage);
                _stores[name] = to;
            }
            return new() { Verb = ActVerb.Move, List = list, To = to };
        }

        public string Sel(string name, string from, string kind, string slot = null, int take = 0, (string prop, bool desc)? sort = null, params Cond[] where)
        {
            var x = new SelectorDef { Id = Defs.NewId("s"), Name = Unique(true, name), Owner = S.Id, From = { from }, Kind = kind, Slot = slot, Where = new CondGroup { Items = where.ToList() } };
            if (take > 0) x.Take = new TakeSpec { N = Expr.Of(take) };
            if (sort != null) x.Sort = new SortSpec { Prop = sort.Value.prop, Desc = sort.Value.desc };
            D.Selectors.Add(x);
            return x.Id;
        }

        /// <summary>流程自己的参数（排班卡片上可改）。</summary>
        public string Param(string name, double value, string unit, string hint)
        {
            var o = new ObjectDef { Id = Defs.NewId("o"), Name = Unique(false, name), Owner = S.Id, Value = value, Unit = unit, Hint = hint };
            D.Objects.Add(o);
            return o.Id;
        }

        /// <summary>自定义属性（全局）：同名同公式的已经有了就沿用。</summary>
        public string Property(string name, string kind, Expr formula)
        {
            var same = D.Properties.FirstOrDefault(p => p.Name == name && p.ItemKind == kind);
            if (same != null) return same.Id;
            var p = new PropertyDef { Id = Defs.NewId("p"), Name = name, ItemKind = kind, Formula = formula };
            D.Properties.Add(p);
            return p.Id;
        }

        public void Rule(string name, ActionDef act, string notEmpty = null, string empty = null, (string sel, int n)? fewer = null, string empty2 = null)
        {
            var r = new WorkRule { Id = Defs.NewId("r"), Name = name, Actions = { act } };
            if (notEmpty != null) r.Pre.Add(new PreCond { Ref = notEmpty, Test = PreTest.NotEmpty });
            if (empty != null) r.Pre.Add(new PreCond { Ref = empty, Test = PreTest.Empty });
            if (empty2 != null) r.Pre.Add(new PreCond { Ref = empty2, Test = PreTest.Empty });
            if (fewer != null) r.Pre.Add(new PreCond { Ref = fewer.Value.sel, Test = PreTest.CountLt, Right = Expr.Of(fewer.Value.n) }); // 库存少于 n 件才做
            S.Rules.Add(r);
        }
    }

    private static RulesData D(B b) => b.D;

    private static Cond Lt(string prop, double n) => Cond.Cmp(prop, "<", Expr.Of(n));
    private static Cond Ge(string prop, double n) => Cond.Cmp(prop, ">=", Expr.Of(n));
    private static Cond Gt(string prop, double n) => Cond.Cmp(prop, ">", Expr.Of(n));

    private static ActionDef Move(string list, string to, string slot = null) => new() { Verb = ActVerb.Move, List = list, To = to, ToSlot = slot == null ? null : SlotNames.Prefix + slot };

    // ---------- 植物特性（DNA 编辑器）：游戏标签，名字由 TagNames 取游戏的 ----------
    private const string Pur = "purifying", Np = "nutrient_poor";
    private static string TraitTag(string t) => "tag:PLANT_TRAIT_" + t;
    private static string TraitName(string t) => TagNames.Name("PLANT_TRAIT_" + t) ?? t;
    private const string Heat = "tag:GENETIC_HEAT_INT", DiskTrait = "str:DNA_DISK_TRAIT_STRING", DiskUses = "tag:DNA_DISK_USES_INT";
    private static Cond Text(string prop, string value) => new() { Prop = prop, Op = "=", Text = value };
    private static Cond NotModified => new() { Prop = Catalog.TypeProp, Op = "!=", Text = Catalog.ItemTypePrefix + "MODIFIED_SEED" };
    private static Cond Le(string prop, double n) => Cond.Cmp(prop, "<=", Expr.Of(n));
    private static Cond LtParam(string prop, string param) => Cond.Cmp(prop, "<", Expr.Ref(TermKind.Const, param));

    /// <summary>
    /// 一台起了设备名的提取机：缺原料（带这个特性、没改良过的种子）、缺空白盘就装上；写好的盘收进基因库；
    /// 基因库里这种盘够了就不再提取。提取机要装好设定为这个特性的精准提取模组（没装时随机提取一种特性）。
    /// </summary>
    private static void Extractor(B b, string device, string trait, string seedKind, int keep, string blank)
    {
        string dev = Places.Ref(device);
        string store = T("example.dna.store");
        var output = b.Sel(L.T("example.dna.sel.output", device), dev, Catalog.Any, slot: SlotNames.Prefix + "eject");
        var target = b.Sel(L.T("example.dna.sel.target_slot", device), dev, Catalog.Any, slot: SlotNames.Prefix + "seed");
        var blankSlot = b.Sel(L.T("example.dna.sel.blank_slot", device), dev, Catalog.Any, slot: SlotNames.Prefix + "output_disk");
        var stock = b.Sel(L.T("example.dna.sel.stock", TraitName(trait)), Places.Shop, "plant_dna_disk", where: new[] { Text(DiskTrait, trait), Cond.Cmp("inStorage", "=", Expr.Of(1)) });
        var raw = b.Idle(L.T("example.dna.sel.raw", TraitName(trait), Catalog.KindName(seedKind)), seedKind, take: 1, Ge(TraitTag(trait), 1), NotModified);
        b.Rule(L.T("example.dna.rule.collect", device), b.ToStore(output, store));
        b.Rule(L.T("example.dna.rule.load_seed", device), Move(raw, dev, "seed"), empty: target, fewer: (stock, keep));
        b.Rule(L.T("example.dna.rule.load_blank", device), Move(blank, dev, "output_disk"), empty: blankSlot, fewer: (stock, keep));
    }

    /// <summary>用 tool 作用于 list（方向由游戏判断）。</summary>
    private static ActionDef Use(string tool, string list, int times = 1) => new() { Verb = ActVerb.Use, Tool = tool, List = list, Times = times };
    private static ActionDef Swap(string list, string with) => new() { Verb = ActVerb.Swap, List = list, To = with };
    private static ActionDef Press(string list, string button, int times = 1) => new() { Verb = ActVerb.Press, List = list, Button = button, Times = times };
    private static ActionDef Toggle(string list) => new() { Verb = ActVerb.Toggle, List = list };

    /// <summary>建一个示例排班（不用设置）。</summary>
    public static Schedule Build(RulesData d, string id)
    {
        var spec = All.First(x => x.Id == id);
        var b = new B(d, spec.Name);
        switch (id)
        {
            case "aqua":
            {
                var ras = b.Machines(T("example.aqua.sel.ras"), "ras");
                var low = b.Sel(T("example.aqua.sel.low_water"), ras, "ras_water_tank", where: Lt("liq:water", 9000));
                var tank = b.Sel(T("example.aqua.sel.tank"), ras, "ras_water_tank");
                // 只取两个桶：水箱空时先把所有闲桶接满会耗光 AP，轮不到加水，鱼当晚就死
                var jugs = b.Idle(T("example.aqua.sel.jugs"), "water_jug", take: 2);
                var hungry = b.Sel(T("example.aqua.sel.hungry_fish"), ras, "fish", where: new[] { Lt("hungerPct", 65), Cond.Of("alive") });
                var feed = b.Idle(T("example.aqua.sel.feed"), "industrial_fish_feed", take: 1);
                var feedIn = b.Sel(T("example.aqua.sel.feed_in"), ras, "industrial_fish_feed", slot: SlotNames.Prefix + "tank");
                // 留种：成年鱼按年龄从小到大，从第 3 条起才收（最年轻的两条留着繁殖）
                // 留种：公、母各留最年轻的一条，其余成年鱼按年龄从小到大，从第 2 条起收
                string Adults(string name, string sex)
                {
                    var id = b.Sel(name, ras, "tilapia", sort: ("age", false), where: new[] { Cond.Of("adult"), Cond.Of("alive"), Cond.Of(sex) });
                    D(b).Selectors.First(x => x.Id == id).Take = new TakeSpec { Mode = TakeMode.From, N = Expr.Of(2) };
                    return id;
                }
                var adultM = Adults(T("example.aqua.sel.adult_m"), "male");
                var adultF = Adults(T("example.aqua.sel.adult_f"), "female");
                var deadFish = b.Sel(T("example.aqua.sel.dead_fish"), ras, "fish", where: Cond.Of("dead"));
                var knives = b.Idle(T("example.aqua.sel.knives"), "blade");
                var meat = b.Sel(T("example.aqua.sel.meat"), ras, "fish_scrap|raw_meat|small_raw_meat|morsel|small_morsel");
                b.Rule(T("example.aqua.rule.fill_jug"), Use(Fixtures.Faucet, jugs), notEmpty: low);
                b.Rule(T("example.aqua.rule.fill_tank"), Use(jugs, tank, times: 2), notEmpty: low);
                b.Rule(T("example.aqua.rule.feed"), Move(feed, ras), notEmpty: hungry, empty: feedIn);
                b.Rule(T("example.aqua.rule.clear_dead"), Use(knives, deadFish));
                b.Rule(T("example.aqua.rule.harvest"), Use(knives, adultM, times: 2));
                b.S.Rules[^1].Actions.Add(Use(knives, adultF, times: 2));
                b.Rule(T("example.aqua.rule.store_meat"), b.ToStore(meat, T("example.aqua.store")));
                break;
            }
            case "hydro":
            {
                var hydro = b.Machines(T("example.hydro.sel.hydro"), "hydroponic");
                // 只给有电、闲着的机器种：没电的备用机放了种子也不长，还会挡住别的机器
                var ready = b.Sel(T("example.hydro.sel.ready"), hydro, Catalog.Self, where: new[] { Cond.Cmp("working", "=", Expr.Of(0)), Cond.Cmp("powered", "=", Expr.Of(1)) });
                var planted = b.Sel(T("example.hydro.sel.planted"), ready, Catalog.Any, slot: SlotNames.Prefix + "seed");
                var seed = b.Idle(T("example.hydro.sel.seed"), "fillerweed_seed|nutrifruit_seed", take: 1);
                var crop = b.Sel(T("example.hydro.sel.crop"), hydro, Catalog.Any, slot: SlotNames.Prefix + "output");
                // 换水和换电池一样：机器里水不多的瓶子和一瓶满的互换位置，换下来的去水龙头接满
                var dry = b.Sel(T("example.hydro.sel.dry"), hydro, Catalog.Any, slot: SlotNames.Prefix + "water", where: Lt("liq:water", 1500));
                var lean = b.Sel(T("example.hydro.sel.lean"), hydro, Catalog.Any, slot: SlotNames.Prefix + "water", where: Lt("liq:mineral", 20));
                var full = b.Idle(T("example.hydro.sel.full"), "large_bottled_water", where: Ge("liq:water", 1500));
                var empty = b.Idle(T("example.hydro.sel.empty"), "large_bottled_water", where: Lt("liq:water", 1500));
                b.Rule(T("example.hydro.rule.harvest"), b.ToStore(crop, T("example.hydro.store")));
                var tablets = b.Idle(T("example.hydro.sel.tablets"), "nutrient_tablet");
                b.Rule(T("example.hydro.rule.plant"), Move(seed, ready), notEmpty: ready, empty: planted);
                b.Rule(T("example.hydro.rule.swap_water"), Swap(dry, full));
                b.Rule(T("example.hydro.rule.fill_water"), Use(Fixtures.Faucet, empty));
                b.Rule(T("example.hydro.rule.add_nutrient"), Use(tablets, lean));
                break;
            }
            case "ranch":
            {
                var cage = b.Machines(T("example.ranch.sel.cage"), "animal_cage");
                var disp = b.Sel(T("example.ranch.sel.dispenser"), cage, Catalog.Container, slot: SlotNames.Prefix + "dispenser");
                var low = b.Sel(T("example.ranch.sel.low_feed"), cage, "livestock_feed_dispenser", slot: SlotNames.Prefix + "dispenser", where: Lt("tag:STORED_KCAL_TAG", 20000));
                var off = b.Sel(T("example.ranch.sel.unblended"), cage, "livestock_feed_dispenser", slot: SlotNames.Prefix + "dispenser", where: Lt("tag:FEED_DISPENSER_AUTO_BLEND_BOOL", 1));
                // 只喂植物饲料：宰杀的肉喂回给鼠会同类相食得病
                var food = b.Idle(T("example.ranch.sel.food"), "fillerweed|nutrifruit", take: 3);
                // 留种：产仔基因最好的 1 只公鼠、2 只母鼠不卖
                var sireM = b.Sel(T("example.ranch.sel.sire_m"), cage, "rat", take: 1, sort: ("gene.litter", true), where: new[] { Cond.Of("alive"), Cond.Of("adult"), Cond.Of("male") });
                var sireF = b.Sel(T("example.ranch.sel.sire_f"), cage, "rat", take: 2, sort: ("gene.litter", true), where: new[] { Cond.Of("alive"), Cond.Of("adult"), Cond.Of("female") });
                var breeders = b.Sel(T("example.ranch.sel.breeders"), sireM, "rat");
                D(b).Selectors.First(x => x.Id == breeders).From.Add(sireF);
                var fat = b.Sel(T("example.ranch.sel.fat"), cage, "rat", where: new[] { Ge("weight", 300), Cond.Of("alive") });
                D(b).Selectors.First(x => x.Id == fat).Minus = breeders;
                var dead = b.Sel(T("example.ranch.sel.dead"), cage, "rat", where: Cond.Of("dead"));
                var poop = b.Sel(T("example.ranch.sel.poop"), cage, "feces_small|feces_medium|feces_large");
                var knives = b.Idle(T("example.ranch.sel.knives"), "blade");
                var meat = b.Sel(T("example.ranch.sel.meat"), cage, "raw_meat|small_raw_meat|morsel|small_morsel");
                b.Rule(T("example.ranch.rule.auto_blend"), Toggle(off));
                b.Rule(T("example.ranch.rule.feed"), Move(food, disp), notEmpty: low);
                b.Rule(T("example.ranch.rule.butcher_dead"), Use(knives, dead));
                b.Rule(T("example.ranch.rule.cull"), Use(knives, fat, times: 2));
                b.Rule(T("example.ranch.rule.store_meat"), b.ToStore(meat, T("example.ranch.store")));
                b.Rule(T("example.ranch.rule.store_poop"), b.ToStore(poop, T("example.ranch.store")));
                break;
            }
            case "gun":
            {
                var printer = b.Machines(T("example.gun.sel.printer"), "printer");
                // 只给有电、闲着的打印机装料：没电的备用机装了也打不出来
                var idle = b.Sel(T("example.gun.sel.idle"), printer, Catalog.Self, where: new[] { Cond.Cmp("working", "=", Expr.Of(0)), Cond.Cmp("powered", "=", Expr.Of(1)) });
                var chipIn = b.Sel(T("example.gun.sel.chip_in"), idle, Catalog.Any, slot: SlotNames.Prefix + "chip");
                var chip = b.Idle(T("example.gun.sel.chip"), "printer_chip_pistol", take: 1);
                var fed = b.Sel(T("example.gun.sel.plastic_in"), idle, "printer_plastic", slot: SlotNames.Prefix + "input");
                var plastic = b.Idle(T("example.gun.sel.plastic"), "printer_plastic", take: 2);
                var output = b.Sel(T("example.gun.sel.output"), printer, Catalog.Any, slot: SlotNames.Prefix + "output");
                b.Rule(T("example.gun.rule.collect"), b.ToStore(output, T("example.gun.store")));
                b.Rule(T("example.gun.rule.chip"), Move(chip, idle), empty: chipIn);
                b.Rule(T("example.gun.rule.feed"), Move(plastic, idle), notEmpty: idle, empty: fed);
                break;
            }
            case "dna_disk":
            {
                var blank = b.Idle(T("example.dna.sel.blank"), "plant_dna_disk", take: 1, Text(DiskTrait, ""));
                Extractor(b, T("example.dna.device.extract_pur"), Pur, "hydroreed_seed", 2, blank);
                Extractor(b, T("example.dna.device.extract_np"), Np, "fillerweed_seed", 3, blank);
                break;
            }
            case "dna_edit":
            {
                // 目标种子由玩家放进【写入机】的目标格；没到目标等级、热度没到上限就写一次，用剩余次数最少的盘
                string writer = T("example.dna.device.writer"), dev = Places.Ref(writer);
                var level = b.Param(T("example.dna.param.target"), 8, "", T("example.dna.param.target.hint"));
                var heatCap = b.Param(T("example.dna.param.heat"), 10, "", T("example.dna.param.heat.hint"));
                var output = b.Sel(L.T("example.dna.sel.output", writer), dev, Catalog.Any, slot: SlotNames.Prefix + "eject");
                var diskSlot = b.Sel(L.T("example.dna.sel.disk_slot", writer), dev, Catalog.Any, slot: SlotNames.Prefix + "input_disk");
                var writable = b.Sel(T("example.dna.sel.writable"), dev, "seed", slot: SlotNames.Prefix + "seed", where: new[] { LtParam(TraitTag(Pur), level), LtParam(Heat, heatCap) });
                var disk = b.Idle(L.T("example.dna.sel.disk", TraitName(Pur)), "plant_dna_disk", take: 1, Text(DiskTrait, Pur));
                D(b).Selectors.First(x => x.Id == disk).Sort = new SortSpec { Prop = DiskUses, Desc = false };
                b.Rule(L.T("example.dna.rule.collect", writer), b.ToStore(output, T("example.dna.store")));
                b.Rule(L.T("example.dna.rule.write", TraitName(Pur)), Move(disk, dev, "input_disk"), notEmpty: writable, empty: diskSlot);
                break;
            }
            case "dna_breed":
            {
                // 有效复杂度 = 复杂度 + 热度：芦苇先天净化水质 4 级，每多一级 +3；营养匮乏每级 −2。成功率 = 95 − 5 × 有效复杂度。
                // ≤ 2 写净化水质；≤ 6 或热度为 0 写营养匮乏（压低复杂度）；热度上来了就送进【育种水培机】，收获的子代（热度 0）回到写入机。
                string writer = T("example.dna.device.writer"), dev = Places.Ref(writer);
                string hydroName = T("example.dna.device.hydro"), hydro = Places.Ref(hydroName);
                var level = b.Param(T("example.dna.param.target"), 8, "", T("example.dna.param.target.hint"));
                var eff = b.Property(T("example.dna.prop.eff"), "hydroreed_seed", new Expr
                {
                    Terms = { Term.N(3), Term.Own(TraitTag(Pur)), Term.N(12), Term.N(2), Term.Own(TraitTag(Np)), Term.Own(Heat) },
                    Ops = { "*", "-", "-", "*", "+" },
                });
                Cond Below() => LtParam(TraitTag(Pur), level);
                var output = b.Sel(L.T("example.dna.sel.output", writer), dev, Catalog.Any, slot: SlotNames.Prefix + "eject");
                var diskSlot = b.Sel(L.T("example.dna.sel.disk_slot", writer), dev, Catalog.Any, slot: SlotNames.Prefix + "input_disk");
                var target = b.Sel(L.T("example.dna.sel.target_slot", writer), dev, Catalog.Any, slot: SlotNames.Prefix + "seed");
                var hSeed = b.Sel(L.T("example.dna.sel.seed_slot", hydroName), hydro, Catalog.Any, slot: SlotNames.Prefix + "seed");
                var child = b.Sel(T("example.dna.sel.child"), hydro, "hydroreed_seed", slot: SlotNames.Prefix + "output", take: 1, sort: (TraitTag(Pur), true));
                var crop = b.Sel(T("example.hydro.sel.crop"), hydro, Catalog.Any, slot: SlotNames.Prefix + "output");
                var first = b.Idle(T("example.dna.sel.first"), "hydroreed_seed", take: 1, NotModified);
                var toPur = b.Sel(L.T("example.dna.sel.can_write", TraitName(Pur)), dev, "hydroreed_seed", slot: SlotNames.Prefix + "seed", where: new[] { Below(), Le(eff, 2) });
                var toNp = b.Sel(L.T("example.dna.sel.can_write", TraitName(Np)), dev, "hydroreed_seed", slot: SlotNames.Prefix + "seed",
                    where: new[] { Below(), Cond.Sub("any", Le(eff, 6), Lt(Heat, 1)) });
                var toBreed = b.Sel(T("example.dna.sel.breed"), dev, "hydroreed_seed", slot: SlotNames.Prefix + "seed", where: new[] { Below(), Ge(Heat, 1), Gt(eff, 6) });
                var purDisk = b.Idle(L.T("example.dna.sel.disk", TraitName(Pur)), "plant_dna_disk", take: 1, Text(DiskTrait, Pur));
                var npDisk = b.Idle(L.T("example.dna.sel.disk", TraitName(Np)), "plant_dna_disk", take: 1, Text(DiskTrait, Np));
                foreach (var dk in new[] { purDisk, npDisk }) D(b).Selectors.First(x => x.Id == dk).Sort = new SortSpec { Prop = DiskUses, Desc = false };
                var water = b.Sel(L.T("example.dna.sel.water_slot", hydroName), hydro, Catalog.Any, slot: SlotNames.Prefix + "water");
                var dry = b.Sel(T("example.hydro.sel.dry"), hydro, Catalog.Any, slot: SlotNames.Prefix + "water", where: Lt("liq:water", 1500));
                var lean = b.Sel(T("example.hydro.sel.lean"), hydro, Catalog.Any, slot: SlotNames.Prefix + "water", where: Lt("liq:mineral", 20));
                var full = b.Idle(T("example.hydro.sel.full"), "large_bottled_water", take: 1, Ge("liq:water", 1500));
                var empty = b.Idle(T("example.hydro.sel.empty"), "large_bottled_water", where: Lt("liq:water", 1500));
                var tablets = b.Idle(T("example.hydro.sel.tablets"), "nutrient_tablet", take: 1);
                string store = T("example.dna.store");
                b.Rule(L.T("example.dna.rule.collect", writer), b.ToStore(output, store));
                b.Rule(T("example.dna.rule.child"), Move(child, dev, "seed"), empty: target);
                b.Rule(T("example.dna.rule.harvest"), b.ToStore(crop, store));
                b.Rule(T("example.dna.rule.first"), Move(first, dev, "seed"), empty: target, empty2: hSeed);
                b.Rule(L.T("example.dna.rule.write", TraitName(Pur)), Move(purDisk, dev, "input_disk"), notEmpty: toPur, empty: diskSlot);
                b.Rule(L.T("example.dna.rule.write", TraitName(Np)), Move(npDisk, dev, "input_disk"), notEmpty: toNp, empty: diskSlot);
                b.Rule(T("example.dna.rule.breed"), Move(toBreed, hydro, "seed"), empty: diskSlot, empty2: hSeed);
                b.Rule(T("example.hydro.rule.fill_water"), Use(Fixtures.Faucet, empty));
                b.Rule(T("example.dna.rule.water"), Move(full, hydro, "water"), empty: water);
                b.Rule(T("example.hydro.rule.swap_water"), Swap(dry, full));
                b.Rule(T("example.hydro.rule.add_nutrient"), Use(tablets, lean));
                break;
            }
            case "tidy":
            {
                // 每类一个任务：全店直接放在储藏区里的这类物品，放进叫这个名字的储藏区（同名的可以有几个，按顺序放进第一个放得下的）。
                // 已经在【药品仓库】里的，放入时本来就会跳过。类别用游戏的商品类型。
                var configs = new[]
                {
                    ("itemType:MEDICAL", "medical", T("example.tidy.store.medical"), T("example.tidy.sel.loose_medical"), T("example.tidy.rule.medical")),
                    ("itemType:FOOD", "food", T("example.tidy.store.food"), T("example.tidy.sel.loose_food"), T("example.tidy.rule.food")),
                };
                foreach (var (kind, _, store, selName, ruleName) in configs)
                {
                    var loose = b.Idle(selName, kind);
                    b.Rule(ruleName, Move(loose, Places.Ref(store)));
                }
                break;
            }
            case "water":
            {
                // 只用有电的：店里可能还放着没电的备用机，往里放桶、按按钮都白做
                var printer = b.Powered(T("example.water.sel.printer"), "bottle_printer");
                var purifier = b.Powered(T("example.water.sel.purifier"), "water_purifier");
                var fountain = b.Powered(T("example.water.sel.fountain"), "vending_fountain");
                var printed = b.Sel(T("example.water.sel.printed"), printer, "small_bottled_water");
                var purBox = b.Sel(T("example.water.sel.pur_box"), purifier, Catalog.Any, slot: SlotNames.Prefix + "container");
                var purDone = b.Sel(T("example.water.sel.pur_done"), purifier, Catalog.Any, slot: SlotNames.Prefix + "container", where: Cond.Cmp("q:CATEGORY_WATER_PURITY", "<=", Expr.Of(2)));
                var fBase = b.Sel(T("example.water.sel.f_base"), fountain, Catalog.Any, slot: SlotNames.Prefix + "base_water");
                var fLow = b.Sel(T("example.water.sel.f_low"), fountain, Catalog.Any, slot: SlotNames.Prefix + "base_water", where: Lt("liq:water", 2000));
                var emptyJugs = b.Idle(T("example.water.sel.empty_jugs"), "water_jug", where: Lt("liq:water", 1000));
                var rawJug = b.Idle(T("example.water.sel.raw_jug"), "water_jug", take: 1, where: new[] { Ge("liq:water", 1000), Cond.Cmp("q:CATEGORY_WATER_PURITY", ">", Expr.Of(2)) });
                var pureJugs = b.Idle(T("example.water.sel.pure_jugs"), "water_jug", where: new[] { Cond.Cmp("q:CATEGORY_WATER_PURITY", "<=", Expr.Of(2)), Ge("liq:water", 300) });
                var onePure = b.Idle(T("example.water.sel.one_pure"), "water_jug", take: 1, where: new[] { Cond.Cmp("q:CATEGORY_WATER_PURITY", "<=", Expr.Of(2)), Ge("liq:water", 3000) });
                var emptyBottles = b.Idle(T("example.water.sel.empty_bottles"), "small_bottled_water", where: Lt("liq:water", 100));
                var fullBottles = b.Idle(T("example.water.sel.full_bottles"), "small_bottled_water", where: Ge("liq:water", 100));
                var cash = b.Sel(T("example.water.sel.cash"), fountain, Catalog.Self, where: Gt("tag:VENDING_CASH", 0));
                b.Rule(T("example.water.rule.cash"), Press(cash, "mech_vending_btn_transfer"));
                b.Rule(T("example.water.rule.print_bottle"), Press(printer, "mech_bottle_printer_btn_print_small", 2), fewer: (emptyBottles, 6));
                b.Rule(T("example.water.rule.take_bottle"), b.ToStore(printed, T("example.water.store")));
                b.Rule(T("example.water.rule.fill_empty"), Use(Fixtures.Faucet, emptyJugs));
                b.Rule(T("example.water.rule.take_purified"), b.ToStore(purDone, T("example.water.store")));
                b.Rule(T("example.water.rule.purify"), Move(rawJug, purifier), empty: purBox);
                b.Rule(T("example.water.rule.bottle"), Use(pureJugs, emptyBottles), notEmpty: pureJugs, fewer: (fullBottles, 10));
                b.Rule(T("example.water.rule.fountain_jug"), Move(onePure, fountain), empty: fBase);
                b.Rule(T("example.water.rule.fountain_refill"), Use(pureJugs, fLow, times: 2), notEmpty: pureJugs);
                break;
            }
        }
        Scope.Normalize(d);
        return b.S;
    }
}
