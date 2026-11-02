using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Simulation;

// 策略模拟器：一个纯数据的小店。物品是属性表，夜间按实测速率变化，流程规则用同一个解释器跑。
// 不碰游戏，所以可以随便放东西、随便跑几天，也能做单元测试。数值来源：游戏实测。

/// <summary>模拟物品。容器（Capacity &gt; 0）可以装别的物品；机器另有一个电池槽。</summary>
public class SimItem
{
    public int Id;

    /// <summary>从游戏复制来的：照着复制的真实物品 uid（问游戏“能否作用”时用）；0 = 不是照实物复制的。</summary>
    public int GameUid;

    /// <summary>在父容器的哪个格子里（"slot:seed"）；为空 = 不分格。从游戏复制。</summary>
    public string InSlot;

    /// <summary>窗口里的格子 (id, 名称)，从游戏复制。</summary>
    public List<(string id, string name)> SlotList = new();

    /// <summary>窗口里的按钮（显示文字），从游戏复制。</summary>
    public List<string> Buttons = new();
    public string Kind = "";
    public string Name = "";
    public Dictionary<string, double> Num = new();
    public Dictionary<string, string> Str = new();
    public SimItem Parent;
    public bool InBatterySlot;
    public List<SimItem> Children = new();

    /// <summary>作为工位成员时的工位 Id。</summary>
    public string Ws;

    /// <summary>设备名（玩家在店里起的名字）；为空 = 没起。</summary>
    public string DeviceName;

    /// <summary>不算在“全店”里（待售的、垃圾、文件）。从游戏复制。</summary>
    public bool OffShop;

    /// <summary>能装几件（不含电池槽）；0 = 不是容器。</summary>
    public int Capacity;

    /// <summary>有电池槽（机器）。</summary>
    public bool HasBatterySlot;

    /// <summary>只接受这些种类（可写种类组）；为空 = 都行。</summary>
    public List<string> Accepts;

    /// <summary>从游戏导入时记下的类别（游戏侧的识别结果），优先于按 Kind 推断。</summary>
    public HashSet<string> TraitsOverride;

    public double N(string k, double d = 0) => Num.TryGetValue(k, out var v) ? v : d;
    public int Used => Children.Count(c => !c.InBatterySlot);
    public int Free => Capacity - Used;
}

/// <summary>模拟物品目录：玩家在模拟器里能放的东西，以及默认属性。</summary>
public static class SimCatalog
{
    public class Entry
    {
        public string Id = "", Kind = "", Group = "";
        public string Name => L.T("sim.item." + Id);
        public int Capacity;
        public bool BatterySlot;
        public List<string> Accepts;
        public Dictionary<string, double> Num = new();
        public Dictionary<string, string> Str = new();
    }

    private static Entry E(string id, string kind, string group, int cap = 0, bool slot = false, List<string> accepts = null,
        Dictionary<string, double> num = null, Dictionary<string, string> str = null) =>
        new() { Id = id, Kind = kind, Group = group, Capacity = cap, BatterySlot = slot, Accepts = accepts, Num = num ?? new(), Str = str ?? new() };

    private static Dictionary<string, double> Rat(double age, double weight) => new()
    {
        ["age"] = age, ["weight"] = weight, ["matingMax"] = 3, ["gene.growth"] = 1, ["gene.litter"] = 4, ["gene.fertility"] = 60, ["gene.immunity"] = 50,
        ["hunger"] = 400, ["maxHunger"] = 500,
    };

    public static readonly List<Entry> All = new()
    {
        // 容器 / 机器
        E("animal_cage", "animal_cage", "容器", cap: 20),
        E("storage", "storage_bay_large", "容器", cap: 40),
        E("toolbox", "toolbox", "容器", cap: 8),
        E("recharger", "recharger", "机器", cap: 4, accepts: new() { "group:battery" }),
        E("moisture_farm", "moisture_farm", "机器", cap: 1, slot: true, accepts: new() { "water_jug" }, num: new() { ["drain"] = 0 }),
        E("water_purifier", "water_purifier", "机器", cap: 1, slot: true, accepts: new() { "water_jug" }, num: new() { ["drain"] = 3 }),
        E("projector", "mirage_projector", "机器", slot: true, num: new() { ["drain"] = 13 }),
        E("machine", "machine", "机器", slot: true, num: new() { ["drain"] = 5 }),
        E("water_pitcher", "water_pitcher", "容器", cap: 1, accepts: new() { "group:filter" }, num: new() { ["capacityMl"] = 3000, ["volumeMl"] = 0, ["purity"] = 0, ["labelOk"] = 1 }),
        // 物品
        E("rat_m", "rat", "鼠", num: Rat(6, 380), str: new() { ["sex"] = "MALE" }),
        E("rat_f", "rat", "鼠", num: Rat(6, 360), str: new() { ["sex"] = "FEMALE" }),
        E("rat_baby", "rat", "鼠", num: Rat(0, 40), str: new() { ["sex"] = "FEMALE" }),
        E("rat_fat", "rat", "鼠", num: Rat(7, 450), str: new() { ["sex"] = "MALE" }),
        E("battery_full", "energy_credit", "电池", num: new() { ["energy"] = 100, ["energyMax"] = 100 }),
        E("battery_empty", "energy_credit", "电池", num: new() { ["energy"] = 5, ["energyMax"] = 100 }),
        E("battery_self", "energy_credit_breeder", "电池", num: new() { ["energy"] = 50, ["energyMax"] = 100 }),
        E("jug_dirty", "water_jug", "水", num: new() { ["capacityMl"] = 6000, ["volumeMl"] = 3000, ["purity"] = 20, ["labelOk"] = 1 }),
        E("jug_empty", "water_jug", "水", num: new() { ["capacityMl"] = 6000, ["volumeMl"] = 0, ["purity"] = 0, ["labelOk"] = 1 }),
        E("filter", "water_filter", "水", num: new() { ["durability"] = 400, ["durabilityMax"] = 400 }),
        E("filter_adv", "water_filter_adv", "水", num: new() { ["durability"] = 400, ["durabilityMax"] = 400 }),
        E("wire_cutter", "wire_cutter", "工具"),
        E("knife", "kitchen_cleaver", "工具"),
        E("labeler", "labeler", "工具"),
        E("meat", "raw_meat", "其他"),
        E("other", "other", "其他"),
    };

    public static Entry Get(string id) => All.FirstOrDefault(e => e.Id == id);

    /// <summary>游戏物品种类 → 目录条目（导入店里的实际物品时用）。</summary>
    public static Entry ForGameKind(string kind) => kind switch
    {
        "rat" => Get("rat_f"),
        "energy_credit" or "energy_credit_ext" => Get("battery_full"),
        "energy_credit_breeder" => Get("battery_self"),
        "water_jug" => Get("jug_empty"),
        "water_pitcher" => Get("water_pitcher"),
        "water_filter" => Get("filter"),
        "water_filter_adv" => Get("filter_adv"),
        "wire_cutter" => Get("wire_cutter"),
        "labeler" => Get("labeler"),
        "kitchen_cleaver" or "combat_knife" or "hatchet" => Get("knife"),
        "animal_cage" => Get("animal_cage"),
        "recharger" => Get("recharger"),
        "moisture_farm" => Get("moisture_farm"),
        "water_purifier" => Get("water_purifier"),
        "mirage_projector" => Get("projector"),
        "storage_bay_large" or "storage_bay" or "machine_bay" or "machine_bay_ext" => Get("storage"),
        "toolbox" => Get("toolbox"),
        _ => null,
    };

    public static bool InGroup(string kind, string group) => group switch
    {
        "battery" => kind is "energy_credit" or "energy_credit_ext" or "energy_credit_breeder",
        "selfCharging" => kind == "energy_credit_breeder",
        "filter" => kind is "water_filter" or "water_filter_adv",
        "liquid" => kind is "water_jug" or "water_pitcher",
        "knife" => kind is "kitchen_cleaver" or "combat_knife" or "hatchet",
        _ => false,
    };
}
