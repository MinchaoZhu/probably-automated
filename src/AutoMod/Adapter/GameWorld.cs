using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Rules;

namespace AutoMod.Adapter;

/// <summary>
/// 游戏适配器：世界端口的游戏实现。类别由绑定表从游戏事实推导，
/// 属性由 PropBindings 读取，四种机制对应游戏的原生交互。物品身份用指针；对象绑定按 uid 解析（uid 在存读档之间不变）。
/// </summary>
public class GameWorld : IWorld
{
    private Dictionary<int, GameItem> _byUid;

    /// <summary>上次扫描时店里的全部物品，和 _byUid 一起更新。</summary>
    private HashSet<GameItem> _all;

    /// <summary>上次扫描在哪一帧；_stale = 那之后店里的物品可能多了或少了而没有记进来。</summary>
    private int _scanFrame = -1;
    private bool _stale;

    public GameWorld()
    {
        Refresh();
    }

    /// <summary>上次扫描时店里的物品数（含嵌套、待售的）。</summary>
    public int ItemCount => _byUid.Count;

    /// <summary>重新收集物品（uid 索引）。动作会创建新物品（例：生肉），解析不到时自动刷新。</summary>
    public void Refresh() => Adopt(GameApi.CollectAll());

    /// <summary>用一次扫描的结果更新索引；跟着店里物品变的缓存一起作废。</summary>
    private void Adopt(List<GameItem> items)
    {
        _byUid = new Dictionary<int, GameItem>(items.Count);
        _all = new HashSet<GameItem>(items);
        _sameKind = null;
        _shop = null;
        _named = null;
        _slots.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            Names.LearnKind(it);
            _byUid[it.uniqueId] = it;
        }
        _scanFrame = UnityEngine.Time.frameCount;
        _stale = false;
    }

    /// <summary>店里任意一件这种物品（按 uid 最小）；没有返回 null。</summary>
    public GameItem AnyOfKind(string kind)
    {
        if (_byUid == null) Refresh();
        return _byUid.Values.Where(i => i.identifier == kind && Alive(i)).OrderBy(i => i.uniqueId).FirstOrDefault();
    }

    public object Fixture(string name) => FixtureItem(name);

    /// <summary>水龙头、下水道（EmporiumEntry 上的固定物品，不在店铺物品树里）。</summary>
    public static GameItem FixtureItem(string name)
    {
        var e = EmporiumEntry.Instance;
        if (e == null) return null;
        var f = name switch { Fixtures.Faucet => e.faucet, Fixtures.Drain => e.drain, _ => null };
        if (f != null) Names.LearnKind(f);
        return f;
    }

    public GameItem ByUid(int uid)
    {
        if (_byUid.TryGetValue(uid, out var it) && Alive(it)) return it;
        foreach (var (id, _) in Fixtures.All)
            if (FixtureItem(id) is { } f && f.uniqueId == uid) return f;
        Refresh();
        return _byUid.TryGetValue(uid, out it) ? it : null;
    }

    public int Day
    {
        get { try { return StoreStation.GetDayCounter(); } catch { return 0; } }
    }

    private static bool Alive(GameItem it)
    {
        try { return it != null && it.parentInventory != null; }
        catch { return false; }
    }

    // ================= 读取 =================

    private List<GameItem> _shop;
    private IReadOnlyList<object> _shopItems;

    /// <summary>全店（GameApi.ShopItems），和 _byUid 一起刷新；其中有物品不在了也重新收集。</summary>
    public IReadOnlyList<object> Shop()
    {
        if (_shop != null)
            for (int i = 0; i < _shop.Count; i++)
                if (!Alive(_shop[i])) { _shop = null; break; }
        if (_shop == null)
        {
            _shop = GameApi.ShopItems();
            _shopItems = _shop.ConvertAll(x => (object)x);
        }
        return _shopItems;
    }

    /// <summary>设备名 → 起了这个名字的东西（按 uid），和 _byUid 一起刷新。</summary>
    private Dictionary<string, List<GameItem>> _named;

    public IReadOnlyList<object> Named(string name)
    {
        if (string.IsNullOrEmpty(name)) return Array.Empty<object>();
        if (_byUid == null) Refresh();
        if (_named == null)
        {
            _named = new Dictionary<string, List<GameItem>>();
            foreach (var it in _byUid.Values)
            {
                if (GameApi.DeviceName(it) is not { } n) continue;
                if (!_named.TryGetValue(n, out var l)) _named[n] = l = new List<GameItem>();
                l.Add(it);
            }
            foreach (var l in _named.Values) l.Sort((a, b) => a.uniqueId.CompareTo(b.uniqueId));
        }
        if (!_named.TryGetValue(name, out var hit)) return Array.Empty<object>();
        var list = new List<object>(hit.Count);
        foreach (var it in hit)
            if (Alive(it)) list.Add(it);
        return list;
    }

    public string DeviceName(object item) => GameApi.DeviceName((GameItem)item);

    /// <summary>机器窗口的格子，和 _byUid 一起刷新（读一次要翻机器窗口的全部界面节点）。</summary>
    private readonly Dictionary<GameItem, List<MachineSlots.Slot>> _slots = new();

    private List<MachineSlots.Slot> SlotsOf(GameItem box)
    {
        if (!_slots.TryGetValue(box, out var list)) _slots[box] = list = MachineSlots.Of(box);
        return list;
    }

    private MachineSlots.Slot FindSlot(GameItem box, string role)
    {
        foreach (var s in SlotsOf(box))
            if (s.Role == role) return s;
        // 未扩建的储藏区只有一页，规则写好后再扩建也不用改
        if (role == SlotNames.FirstTab && StorageExpansionHelper.CanExpand(box) && box.contentWindow?.childElement is GameGridInventory grid)
            return new MachineSlots.Slot { Role = role, Inventories = { grid } };
        return null;
    }

    public IReadOnlyList<object> Contents(object box, string role)
    {
        var b = (GameItem)box;
        var list = new List<object>();
        if (role == "battery")
        {
            var s = GameApi.BatterySlot(b);
            if (s == null) return list;
            foreach (var c in GameApi.DirectChildren(b))
                if (ReferenceEquals(c.parentInventory, s)) list.Add(c);
            return list;
        }
        if (role == "filter")
        {
            foreach (var c in GameApi.DirectChildren(b))
                if (Catalog.HasTrait(this, c, "filter")) list.Add(c);
            return list;
        }
        if (role != null && role.StartsWith(SlotNames.Prefix))
        {
            var slot = FindSlot(b, role[SlotNames.Prefix.Length..]);
            if (slot == null) return list;
            foreach (var c in GameApi.DirectChildren(b))
                foreach (var inv in slot.Inventories)
                    if (ReferenceEquals(c.parentInventory, inv)) { list.Add(c); break; }
            return list;
        }
        foreach (var c in GameApi.DirectChildren(b)) list.Add(c);
        return list;
    }

    public object Parent(object item) => GameApi.ParentContainer((GameItem)item);
    public bool Exists(object item) => Alive((GameItem)item);
    public object Key(object item) => item;
    public int Uid(object item) => ((GameItem)item).uniqueId;

    private readonly Dictionary<GameItem, HashSet<string>> _traits = new();


    public string Kind(object item) => ((GameItem)item).identifier;

    public IReadOnlyCollection<string> Traits(object item)
    {
        var it = (GameItem)item;
        if (!_traits.TryGetValue(it, out var t)) _traits[it] = t = TraitBindings.TraitsOf(it);
        return t;
    }

    public IReadOnlyCollection<string> KindTraits(string kind) => TraitBindings.KindTraits(kind);

    public double? Num(object item, string prop)
    {
        var it = (GameItem)item;
        switch (prop)
        {
            case "count": return TraitBindings.IsContainer(it) ? Capacity(it).used : null;
            case "free": return TraitBindings.IsContainer(it) ? Capacity(it).free : null;
            case "cap": return TraitBindings.IsContainer(it) ? Capacity(it).cap : null;
        }
        return PropBindings.Get(it, prop).Num;
    }

    public string Str(object item, string prop) => PropBindings.Get((GameItem)item, prop).Str;

    /// <summary>
    /// 容量：槽位背包按格数计；网格背包按空格数 ÷ 现有物品的常见占格数估算。
    /// 机器的电池槽不算进容量。无法计算时为空。
    /// </summary>
    public static (double? used, double? free, double? cap) Capacity(GameItem box)
    {
        try
        {
            var invs = ItemFacts.Inventories(box);
            var battery = GameApi.BatterySlot(box);
            int slotCap = 0, slotUsed = 0, gridCells = 0, gridUsedCells = 0, gridItems = 0;
            var areas = new List<int>();
            foreach (var node in invs)
            {
                if (ReferenceEquals(node, battery)) continue;
                var slot = (node as GameSlotInventory);
                if (slot != null)
                {
                    slotCap++;
                    if (slot.childItem != null) slotUsed++;
                    continue;
                }
                var grid = (node as GameGridInventory);
                if (grid == null) continue;
                var shape = grid.inventoryShape;
                if (shape != null) gridCells += FreeCells(shape);
                var items = grid.childItems;
                if (items != null)
                    for (int k = 0; k < items.Count; k++)
                    {
                        var s = items[k]?.shape;
                        int a = s == null ? 1 : Math.Max(1, Cells(s));
                        gridUsedCells += a;
                        areas.Add(a);
                        gridItems++;
                    }
            }
            if (slotCap == 0 && gridCells == 0) return (null, null, null);
            int unit = areas.Count == 0 ? 1 : areas.GroupBy(a => a).OrderByDescending(g => g.Count()).First().Key;
            int gridFree = Math.Max(0, gridCells - gridUsedCells) / Math.Max(1, unit);
            int used = slotUsed + gridItems;
            return (used, slotCap - slotUsed + gridFree, used + slotCap - slotUsed + gridFree);
        }
        catch { return (null, null, null); }
    }

    /// <summary>库存网格里 0 = 可放置格（实测储藏区 17×10 全为 0）；万一没有 0 就按整块算。</summary>
    private static int FreeCells(GridShape s)
    {
        int n = 0;
        int w = s.width, h = s.height;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (s.GetLocal(x, y) == 0) n++;
        return n == 0 ? w * h : n;
    }

    /// <summary>物品形状里非 0 = 占用格。</summary>
    private static int Cells(GridShape s)
    {
        int n = 0;
        int w = s.width, h = s.height;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (s.GetLocal(x, y) != 0) n++;
        return n;
    }

    public bool Fits(object box, object item, string slot = null)
    {
        try { return Node((GameItem)box, slot, (GameItem)item) is { } node && GraphUtils.CanAccept(node, (GameItem)item); }
        catch { return false; }
    }

    /// <summary>放东西的位置：整个容器（游戏自己挑格子），或指定格子里第一个放得下的库存（一个格子可以有几个库存，都放不下时给第一个）；格子不存在返回 null。</summary>
    private GraphNodeStorage Node(GameItem box, string slot, GameItem item)
    {
        if (string.IsNullOrEmpty(slot)) return box;
        var invs = FindSlot(box, slot.StartsWith(SlotNames.Prefix) ? slot[SlotNames.Prefix.Length..] : slot)?.Inventories;
        return invs?.FirstOrDefault(inv => GraphUtils.CanAccept(inv, item)) ?? invs?.FirstOrDefault();
    }

    public string Describe(object item)
    {
        var it = (GameItem)item;
        string name = GameApi.Describe(it);
        if (it.identifier == "rat")
        {
            string sex = Str(it, "sex") == "MALE" ? Domain.L.T("sim.sex.male") : Domain.L.T("sim.sex.female");
            string neutered = Num(it, "neutered") == 1 ? Domain.L.T("sim.desc.neutered") : "";
            string dead = Num(it, "dead") == 1 ? Domain.L.T("sim.desc.dead") : "";
            return Domain.L.T("sim.desc.rat", sex, it.uniqueId, Num(it, "weight")?.ToString("0") ?? "0", Num(it, "age")?.ToString("0") ?? "0", neutered, dead);
        }
        var traits = Catalog.Closure(Traits(it));
        if (traits.Contains("fish"))
        {
            // 鱼：名字 + 雌雄、体重、天数（游戏提示里的同一组值）
            string sex = Str(it, "sex") switch { "MALE" => Domain.L.T("fish.male"), "FEMALE" => Domain.L.T("fish.female"), _ => "" };
            string dead = Num(it, "dead") == 1 ? Domain.L.T("fish.dead") : "";
            return Domain.L.T("fish.desc", name, sex, Num(it, "weight")?.ToString("0") ?? "0", Num(it, "age")?.ToString("0") ?? "0", dead);
        }
        if (traits.Contains("battery")) return Domain.L.T("battery.desc", name, (Num(it, "energy") * 100 / Math.Max(1, Num(it, "energyMax") ?? 100))?.ToString("0") ?? "0");
        if (traits.Contains("liquid")) return Domain.L.T("liquid.desc", name, Num(it, "volumeMl")?.ToString("0") ?? "0", Num(it, "purity")?.ToString("0") ?? "0");
        return name;
    }

    // ================= 命令：放置、更换、作用、标注 =================

    private string BoxName(GameItem b) => DistinctName(b);

    private Dictionary<string, List<int>> _sameKind;

    /// <summary>
    /// 容器名：起了名字的就是那个名字；没起名字、店里有几个同种容器时加编号（按 uid 顺序），例如“储藏区（扩建）#2”。
    /// </summary>
    public string DistinctName(GameItem it) => GameApi.DeviceName(it) ?? KindName(it);

    /// <summary>不看起的名字：物品名，店里有几个同种容器时加编号（设备名列表里说明这个名字是哪台）。</summary>
    public string KindName(GameItem it)
    {
        string name = it.name;
        if (_sameKind == null)
        {
            if (_byUid == null) Refresh();
            _sameKind = _byUid.Values.Where(x => GameApi.Tag(x, "CONTAINER_TAG") != null || GameApi.Tag(x, "MACHINE") != null)
                .GroupBy(x => x.identifier).Where(g => g.Count() > 1)
                .ToDictionary(g => g.Key, g => g.Select(x => x.uniqueId).OrderBy(u => u).ToList());
        }
        return _sameKind.TryGetValue(it.identifier, out var uids) && uids.IndexOf(it.uniqueId) is var i and >= 0 ? $"{name}#{i + 1}" : name;
    }

    private static Effect Moved(GameItem it, GameItem from, GameItem to) =>
        new() { Kind = EffectKind.Moved, Uid = it.uniqueId, Item = GameApi.Describe(it), From = from?.uniqueId ?? 0, To = to?.uniqueId ?? 0 };

    public StepResult Place(object item, object box, string slot = null)
    {
        var it = (GameItem)item;
        var b = (GameItem)box;
        try
        {
            var from = GameApi.ParentContainer(it);
            var node = Node(b, slot, it);
            if (node == null) return new StepResult { Ok = false, Code = FailCode.DestFull, Reason = Domain.L.T("gameworld.box_no_slot", BoxName(b), SlotNames.Name(slot, new[] { ((GameItem)b).identifier })) };
            if (!GraphUtils.CanAccept(node, it)) return new StepResult { Ok = false, Code = FailCode.DestFull, Reason = Domain.L.T("gameworld.box_full", BoxName(b)) };
            int rest = GraphUtils.TryAcceptAll(node, it, -1);
            _slots.Clear(); // 放进机器的东西（例如模块）可能让它的窗口多出格子
            // 只是换了地方时店里的物品还是那些；物品没了（游戏把它并进了别的物品）或只放进去一部分时，下次作用前重新扫描
            if (rest != 0 || !Alive(it)) _stale = true;
            if (rest != 0) return new StepResult { Ok = false, Code = FailCode.DestFull, Reason = Domain.L.T("gameworld.box_cannot_accept", BoxName(b)) };
            var r = StepResult.Done(Domain.L.T("gameworld.move_to", BoxName(b)));
            r.Effects.Add(Moved(it, from, b));
            return r;
        }
        catch (Exception ex)
        {
            _stale = true;
            return StepResult.Fail(Domain.L.T("gameworld.move_error", ex.Message), FailCode.Error);
        }
    }

    public StepResult Swap(object a, object b)
    {
        var t = (GameItem)a;
        var r = (GameItem)b;
        try
        {
            var (tp, rp) = (GameApi.ParentContainer(t), GameApi.ParentContainer(r));
            if (!GraphUtils.SwapItemsInPlace(t, r)) return StepResult.Fail(Domain.L.T("gameworld.swap_not_allowed"));
            _slots.Clear();
            var res = StepResult.Done(Domain.L.T("gameworld.swap_with", Describe(r)));
            res.Effects.Add(Moved(r, rp, tp));
            res.Effects.Add(Moved(t, tp, rp));
            return res;
        }
        catch (Exception ex)
        {
            _stale = true;
            return StepResult.Fail(Domain.L.T("gameworld.swap_error", ex.Message), FailCode.Error);
        }
    }

    // ================= 店里存放着的物品、作用 =================

    public IReadOnlyList<object> Stock()
    {
        if (_byUid == null) Refresh();
        return _byUid.Values.Where(it =>
            {
                if (!Alive(it)) return false;
                var parent = GameApi.ParentContainer(it);
                return parent != null && Catalog.Matches(this, parent, "storage");
            })
            .OrderBy(it => it.uniqueId).Cast<object>().ToList();
    }

    /// <summary>执行时游戏要打开界面的（动作, 种类）：这一局里不再尝试。动作 = 工具种类，或触发方式（Triggers）。</summary>
    private static readonly HashSet<(string act, string kind)> NeedsPlayer = new();

    /// <summary>新记住一对（动作|种类）时通知（App 写进存档）。</summary>
    public static Action<string> NeedsPlayerAdded;

    /// <summary>读档时恢复记住的（“动作|种类”）。</summary>
    public static void LoadNeedsPlayer(IEnumerable<string> keys)
    {
        NeedsPlayer.Clear();
        foreach (var k in keys ?? Enumerable.Empty<string>())
        {
            int bar = k.IndexOf('|');
            if (bar > 0) NeedsPlayer.Add((k[..bar], k[(bar + 1)..]));
        }
    }

    public static int NeedsPlayerCount => NeedsPlayer.Count;

    private static void RememberNeedsPlayer((string act, string kind) key)
    {
        if (NeedsPlayer.Add(key)) NeedsPlayerAdded?.Invoke($"{key.act}|{key.kind}");
    }

    public bool CanApply(object tool, object target)
    {
        try
        {
            var a = (GameItem)tool;
            var t = (GameItem)target;
            return !NeedsPlayer.Contains((a.identifier, t.identifier)) && a.CanTarget(t) && a.MayTarget(t);
        }
        catch { return false; }
    }

    public IReadOnlyList<(string id, string name)> Slots(object box)
    {
        try { return MachineSlots.Of((GameItem)box).Where(s => !s.Hidden).Select(s => (SlotNames.Prefix + s.Role, s.Label ?? SlotNames.Name(s.Role))).ToList(); }
        catch { return new List<(string, string)>(); }
    }

    /// <summary>物品在父容器的哪个格子里（"slot:角色"）；不分格的为空。</summary>
    public static string SlotOf(GameItem item)
    {
        var parent = GameApi.ParentContainer(item);
        if (parent == null) return null;
        var slot = MachineSlots.Of(parent).FirstOrDefault(s => s.Inventories.Any(inv => ReferenceEquals(item.parentInventory, inv)));
        return slot == null ? null : SlotNames.Prefix + slot.Role;
    }

    public IReadOnlyList<string> Buttons(object item)
    {
        try { return MachineButtons.Of((GameItem)item).Select(MachineButtons.Id).Where(l => l != "?").Distinct().ToList(); }
        catch { return new List<string>(); }
    }

    public bool CanTrigger(object item, string how)
    {
        try
        {
            var it = (GameItem)item;
            if (NeedsPlayer.Contains((how, it.identifier))) return false;
            if (how.StartsWith(Triggers.ButtonPrefix)) return MachineButtons.Find(it, how[Triggers.ButtonPrefix.Length..]) is { } b && MachineButtons.Pressable(b);
            return how == Triggers.Toggle ? it.CanToggleSlotItem() && it.MayToggleSlotItem() : it.CanActivateSlotItem() && it.MayActivateSlotItem();
        }
        catch { return false; }
    }

    /// <summary>作用一次（游戏的 Target）。</summary>
    public StepResult Apply(object tool, object target, IReadOnlyList<object> outputs)
    {
        var a = (GameItem)tool;
        var t = (GameItem)target;
        string name = GameApi.DefaultName(a);
        return Observe(t, a, () => a.Target(t), (a.identifier, t.identifier), $"{name} → {GameApi.DefaultName(t)}", Domain.L.T("gameworld.need_player_ui", name), outputs);
    }

    /// <summary>激活（双击使用）/ 开关 / 按机器按钮一次。</summary>
    public StepResult Trigger(object item, string how, IReadOnlyList<object> outputs)
    {
        var it = (GameItem)item;
        string name = GameApi.DefaultName(it);
        if (how.StartsWith(Triggers.ButtonPrefix))
        {
            string btnId = how[Triggers.ButtonPrefix.Length..];
            var button = MachineButtons.Find(it, btnId);
            string display = MachineButtons.DisplayName(btnId);
            if (button == null || !MachineButtons.Pressable(button)) return StepResult.Fail(Domain.L.T("gameworld.no_button", name, display), FailCode.NotAllowed);
            return Observe(it, null, () => button.Select(), (how, it.identifier), Domain.L.T("gameworld.press_button", display), Domain.L.T("gameworld.button_needs_player", name, display), outputs);
        }
        bool toggle = how == Triggers.Toggle;
        string verb = toggle ? Domain.L.T("op.toggle") : Domain.L.T("op.activate");
        return Observe(it, null, () => { if (toggle) it.ToggleSlotItem(); else it.ActivateSlotItem(); },
            (how, it.identifier), verb, Domain.L.T("gameworld.action_needs_player", name, verb), outputs);
    }

    /// <summary>参与一次交互的一方：执行前的属性、标签、位置。</summary>
    private sealed class Side
    {
        public GameItem It;
        public int Uid;
        public string Name, Desc;
        public GameItem Parent;
        public bool Fixture;
        public Dictionary<string, string> Props, Tags;
    }

    private Side Before(GameItem it) => it == null ? null : new Side
    {
        It = it, Uid = it.uniqueId, Name = GameApi.DefaultName(it), Desc = GameApi.Describe(it), Parent = GameApi.ParentContainer(it),
        Fixture = Fixtures.All.Any(f => ReferenceEquals(FixtureItem(f.id), it)), Props = KnownProps(it), Tags = TagValues(it),
    };

    /// <summary>
    /// 执行一次游戏交互并如实记录（效果由游戏决定）：两边（对象、工具）谁消失了、新出现的物品（放进产物去向）、
    /// mod 认识的属性的变化，其他标签变化只计数。什么都没变 = NoEffect；游戏为此打开了界面 = NeedsPlayer（关掉界面，记住 key）。
    /// 水龙头、下水道这类固定设施不在店铺物品树里，不算消失。
    /// </summary>
    private StepResult Observe(GameItem t, GameItem tool, Action act, (string, string) key, string label, string needsPlayer, IReadOnlyList<object> outputs)
    {
        try
        {
            // 执行前店里有哪些物品：上次扫描之后（同一帧里）只有这个世界自己的动作动过店里，结果都已记进来，不用再扫
            if (_stale || _all == null || _scanFrame != UnityEngine.Time.frameCount) Refresh();
            var before = _all;
            var sides = new[] { Before(t), Before(tool) }.Where(x => x != null).ToList();
            int cash0 = Cash.Current;
            var ui = GameUi.Snapshot();
            var dialog = GameDialog.Snapshot();

            _stale = true; // 下面扫描完才知道店里现在有什么
            act();

            // 游戏用通用对话框回话：单按钮是“做不了”的提示（缺配料、没水……），照游戏的话报原因，下次条件满足了照样做；
            // 双按钮是要玩家确认（例如清空），学徒不替玩家确认
            if (dialog.Opened() is { } msg)
            {
                if (!msg.question) return StepResult.Fail(Domain.L.T("gameworld.game_tip") + msg.text, FailCode.NotAllowed);
                RememberNeedsPlayer(key);
                return StepResult.Fail(Domain.L.T("gameworld.confirm_needs_player", msg.text), FailCode.NeedsPlayer);
            }
            if (ui.Opened(close: true) > 0)
            {
                RememberNeedsPlayer(key);
                return StepResult.Fail(needsPlayer, FailCode.NeedsPlayer);
            }

            var now = GameApi.CollectAll();
            Adopt(now);
            var nowSet = _all;
            var created = now.Where(i => !before.Contains(i)).ToList();
            var res = new StepResult { Ok = true, Label = label, Changes = new List<StepChange>(), OtherParts = new List<string>() };
            int other = 0;
            bool any = created.Count > 0;
            var changedSides = new List<(Side side, List<(string id, string old, string cur)> changes)>();
            foreach (var x in sides)
            {
                bool gone = !Alive(x.It) || (!x.Fixture && !nowSet.Contains(x.It));
                if (gone)
                {
                    any = true;
                    res.OtherParts.Add(Domain.L.T("report.consume_item", x.Name));
                    res.Effects.Add(new Effect { Kind = EffectKind.Removed, Uid = x.Uid, Item = x.Desc, From = x.Parent?.uniqueId ?? 0 });
                    continue;
                }
                var changes = x.Props.Where(p => p.Value != PropValue(x.It, p.Key)).Select(p => (p.Key, p.Value, PropValue(x.It, p.Key))).ToList();
                other += Math.Max(0, Diff(x.Tags, TagValues(x.It)) - changes.Count);
                // 水量和剩余容量是同一件事的两面：有水量就不写剩余容量
                if (changes.Any(c => c.Key == "volumeMl")) changes.RemoveAll(c => c.Key == "freeMl");
                if (changes.Count > 0) changedSides.Add((x, changes));
            }
            int cashDelta = Cash.Current - cash0;
            any |= changedSides.Count > 0 || other > 0 || cashDelta != 0;
            if (!any) return StepResult.Fail(Domain.L.T("report.no_change"), FailCode.NoEffect);

            foreach (var (x, changes) in changedSides)
            {
                // 两边都变了（倒水：两个容器的水量）只写对象那边，工具那边的变化不言自明（效果照样记）
                bool shown = changedSides.Count == 1 || ReferenceEquals(x, changedSides[0].side);
                foreach (var (id, old, cur) in changes)
                {
                    if (shown) res.Changes.Add(new StepChange(Catalog.PropName(id), old, cur));
                    res.Effects.Add(new Effect { Kind = EffectKind.Changed, Uid = x.Uid, Item = x.Desc, Prop = id, Old = double.TryParse(old, out var o) ? o : null, New = double.TryParse(cur, out var n) ? n : null });
                }
            }
            if (cashDelta != 0) res.OtherParts.Add(Domain.L.T("report.cash_delta", cashDelta > 0 ? $"+{cashDelta}" : $"{cashDelta}"));
            int moved = 0;
            foreach (var o in created)
            {
                GameItem dest = null;
                if (outputs != null)
                    foreach (GameItem b in outputs)
                        if (Fits(b, o) && Place(o, b).Ok) { dest = b; moved++; break; }
                res.Effects.Add(new Effect { Kind = EffectKind.Created, Uid = o.uniqueId, Item = GameApi.Describe(o), To = (dest ?? GameApi.ParentContainer(o))?.uniqueId ?? 0 });
            }
            if (created.Count > 0)
            {
                string prodNames = Domain.L.Join("list", created.GroupBy(o => GameApi.DefaultName(o)).Select(g => g.Count() > 1 ? Domain.L.T("report.product_group", g.Key, g.Count()) : g.Key));
                string unplaced = outputs != null && moved < created.Count ? Domain.L.N("report.product_unplaced", created.Count - moved) : "";
                res.OtherParts.Add(Domain.L.T("report.product_output", prodNames) + unplaced);
            }
            res.Text = Domain.Rules.Text.FormatMerged(label, res.Changes, res.OtherParts);
            return res;
        }
        catch (Exception ex) { return StepResult.Fail(Domain.L.T("gameworld.action_error", label, ex.Message), FailCode.Error); }
    }

    /// <summary>对象上 mod 认识的属性（目录里适用于它的）此刻的值，比较用文字。</summary>
    private Dictionary<string, string> KnownProps(GameItem it) =>
        Catalog.PropsFor(TraitBindings.PrimaryTrait(it) ?? Catalog.Any, this).Distinct().ToDictionary(id => id, id => PropValue(it, id));

    private string PropValue(GameItem it, string id)
    {
        var n = Num(it, id);
        return n.HasValue ? n.Value.ToString("0.##") : Str(it, id);
    }

    private static Dictionary<string, string> TagValues(GameItem it)
    {
        var values = new Dictionary<string, string>();
        var dict = it.state?.dict;
        if (dict == null) return values;
        foreach (var kv in dict) values[kv.Key] = $"{kv.Value.valueEnabled}|{kv.Value.valueBool}|{ItemFacts.Number(kv.Value)}|{kv.Value.valueString}";
        return values;
    }

    private static int Diff(Dictionary<string, string> a, Dictionary<string, string> b) =>
        a.Keys.Union(b.Keys).Count(k => !a.TryGetValue(k, out var x) || !b.TryGetValue(k, out var y) || x != y);

    public StepResult Annotate(object item, string text, bool on)
    {
        var it = (GameItem)item;
        if (on) GameApi.SetString(it, PropBindings.MarkPrefix + text, "1");
        else GameApi.RemoveTag(it, PropBindings.MarkPrefix + text);
        return StepResult.Done(on ? Domain.L.T("gameworld.add_mark", text) : Domain.L.T("gameworld.remove_mark", text));
    }
}
