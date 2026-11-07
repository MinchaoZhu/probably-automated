using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Simulation;

namespace AutoMod.Adapter;

/// <summary>
/// 把店铺当前状态复制成模拟世界：所有对象绑定的容器及其内容物（含子容器、槽位），以及存放着工具的容器。
/// 物品 Id 取游戏 uid，对象绑定在模拟世界里同样有效。
/// </summary>
public static class SimImport
{
    private static readonly string[] NumProps =
    {
        "age", "weight", "matingCount", "matingMax", "hunger", "maxHunger", "health", "maxHealth", "neutered", "dead", "mature", "gravid",
        "gene.growth", "gene.litter", "gene.fertility", "gene.immunity", "gene.longevity",
        "energy", "energyMax", "durability", "durabilityMax", "volumeMl", "purity", "labelOk", "working", "progress", "progressMax",
        "calories", "growPct", "plantHealth", "waterUse", "nutrientMin", "nutrientMax", "repairable",
    };

    public static SimWorld FromGame(RulesData rules, int seed = 1)
    {
        var sim = new SimWorld(seed);
        var gw = new GameWorld();
        sim.Day = gw.Day;
        // 全店复制进来（筛选器从全店找）：每件只复制一次，从最外层的容器往里复制
        var have = new HashSet<int>();
        void Import(GameItem box)
        {
            if (box == null || have.Contains(box.uniqueId)) return;
            sim.Roots.Add(Copy(sim, gw, box, 0, have));
        }
        var shop = GameApi.ShopItems();
        var inShop = new HashSet<int>(shop.Select(x => x.uniqueId));
        foreach (var it in shop)
        {
            var top = it;
            while (GameApi.ParentContainer(top) is { } p) top = p;
            Import(top);
        }
        // 复制进来但不算全店的（售货机里的货）照样在，只是全店找不到
        foreach (var x in sim.All()) x.OffShop = !inShop.Contains(x.GameUid);
        // 能不能作用由游戏回答：按复制时的 uid 找回真实物品来问
        foreach (var (id, _) in Fixtures.All)
            if (GameWorld.FixtureItem(id) is { } f)
            {
                var x = Copy(sim, gw, f, 99, have);
                sim.FixtureItems[id] = x;
            }
        // 能不能作用、激活由游戏回答：按复制时记下的 uid 找回真实物品来问；没有对应实物的（从原型添加的）当作允许，执行时才知道
        GameItem Real(SimItem x) => x.GameUid == 0 ? null : gw.ByUid(x.GameUid) ?? gw.AnyOfKind(x.Kind);
        sim.CanApplyRule = (tool, target) => Real(tool) is not { } a || Real(target) is not { } t || gw.CanApply(a, t);
        sim.CanTriggerRule = (item, how) => Real(item) is { } it ? gw.CanTrigger(it, how) : !how.StartsWith(Triggers.ButtonPrefix) || item.Buttons.Contains(how[Triggers.ButtonPrefix.Length..]);
        return sim;
    }

    /// <summary>
    /// 模拟器“添加物品”：店里有这种物品就照一件实物复制（类别、属性、容量都和店里的一样，内容物不复制），
    /// 没有就照原型复制。返回的物品还没放进任何容器。
    /// </summary>
    public static SimItem NewOfKind(SimWorld sim, string kind)
    {
        var gw = new GameWorld();
        var real = gw.AnyOfKind(kind);
        var src = real ?? ItemFacts.CreatePrototype(kind);
        if (src == null) return null;
        try
        {
            var x = Copy(sim, gw, src, 99, new HashSet<int>());
            x.Id = sim.NewId();
            x.GameUid = real?.uniqueId ?? 0;
            return x;
        }
        finally { if (real == null) try { src.Destroy(); } catch { } }
    }

    /// <summary>可添加的种类：店里有的（带件数）在前，其余按名字；filter 按名字或 id 筛。</summary>
    public static List<(string kind, string name, int inShop)> Kinds(string filter)
    {
        var counts = GameApi.CollectAll().GroupBy(i => i.identifier).ToDictionary(g => g.Key, g => g.Count());
        if (!string.IsNullOrWhiteSpace(filter)) Names.LearnAll();
        var ids = string.IsNullOrWhiteSpace(filter) ? counts.Keys : ItemFacts.DirectoryOf.Keys.Concat(counts.Keys).Distinct();
        return ids.Select(k => (kind: k, name: Names.Kind(k), inShop: counts.GetValueOrDefault(k)))
            .Where(x => string.IsNullOrWhiteSpace(filter) || x.name.Contains(filter.Trim()) || x.kind.Contains(filter.Trim()))
            .OrderByDescending(x => x.inShop > 0).ThenBy(x => x.name, StringComparer.Ordinal).ToList();
    }

    private static SimItem Copy(SimWorld sim, GameWorld gw, GameItem it, int depth, HashSet<int> have)
    {
        have.Add(it.uniqueId);
        var entry = SimCatalog.ForGameKind(it.identifier);
        var x = new SimItem
        {
            Id = it.uniqueId, GameUid = it.uniqueId, Kind = it.identifier, Name = GameApi.DefaultName(it), DeviceName = GameApi.DeviceName(it),
            Accepts = entry?.Accepts, HasBatterySlot = GameApi.BatterySlot(it) != null,
            Num = new Dictionary<string, double>(entry?.Num ?? new Dictionary<string, double>()),
            TraitsOverride = TraitBindings.TraitsOf(it),
            Buttons = gw.Buttons(it).ToList(),
            SlotList = gw.Slots(it).ToList(),
            InSlot = gw.SlotOf(it),
        };
        foreach (var p in NumProps)
        {
            var v = PropBindings.Get(it, p).Num;
            if (v.HasValue) x.Num[p] = v.Value;
        }
        var free = PropBindings.Get(it, "freeMl").Num;
        if (x.Num.ContainsKey("volumeMl") && free.HasValue) x.Num["capacityMl"] = x.Num["volumeMl"] + free.Value;
        // 游戏标签照原样带上，条件里的“标签 …”在模拟里也能求值
        foreach (var raw in PropBindings.RawTagProps(new[] { it }))
        {
            var v = PropBindings.Get(it, raw);
            if (GameTags.IsTextProp(raw)) { if (v.Str != null) x.Str[raw] = v.Str; }
            else if (v.Num.HasValue) x.Num[raw] = v.Num.Value;
        }
        // 夜间模拟要的游戏数据：机器速度、种子的所需进度和收成、鱼的成年天数
        try
        {
            var speed = GameApi.Tag(it, "CURRENT_PROCESSING_SPEED_TAG");
            if (speed != null) x.Num["speed"] = ItemFacts.Number(speed);
            if (GameApi.Tag(it, "SEED_ITEM_TAG") != null)
            {
                // 各项分开读：游戏的某个函数出错不影响其他项（收成种类读不到时模拟出的收成没有名字）
                var crop = Try(() => HydroponicHelper.GetHarvestItemId(it));
                if (string.IsNullOrEmpty(crop)) crop = GameApi.GetString(it, "HARVEST_ITEM_ID");
                if (!string.IsNullOrEmpty(crop)) x.Str["harvestKind"] = crop;
                x.Num["growProgress"] = GameApi.Tag(it, "GROWING_PROGRESS") is { } gp ? ItemFacts.Number(gp) : 0;
                if (Try(() => (double?)HydroponicHelper.GetRequiredProgress(it)) is { } req) x.Num["growRequired"] = req;
                if (Try(() => (double?)HydroponicHelper.GetBaseHarvestQuantity(it)) is { } qty) x.Num["harvestQty"] = qty;
            }
            if ((AquacultureHelper.IsFish(it) || AquacultureHelper.IsGroup(it)) && AquacultureHelper.GetSpecies(it) is { } sp && sp.stageStartDays != null && sp.stageStartDays.Length > (int)FishStage.Adult)
                x.Num["adultAge"] = sp.stageStartDays[(int)FishStage.Adult];
        }
        catch { }
        var sex = PropBindings.Get(it, "sex").Str;
        if (!string.IsNullOrEmpty(sex)) x.Str["sex"] = sex;
        if (TraitBindings.IsContainer(it))
        {
            var (used, freeSlots, cap) = GameWorld.Capacity(it);
            x.Capacity = cap.HasValue ? (int)cap.Value : GameApi.DirectChildren(it).Count + 4;
            if (depth < 3)
            {
                var slot = GameApi.BatterySlot(it);
                foreach (var child in GameApi.DirectChildren(it))
                {
                    if (have.Contains(child.uniqueId)) continue;
                    var c = Copy(sim, gw, child, depth + 1, have);
                    sim.Attach(c, x, slot != null && ReferenceEquals(child.parentInventory, slot));
                }
            }
        }
        return x;
    }

    private static T Try<T>(Func<T> f)
    {
        try { return f(); }
        catch (Exception ex) { AutoMod.Domain.Log.Warn($"[SimImport] {ex.Message}"); return default; }
    }
}
