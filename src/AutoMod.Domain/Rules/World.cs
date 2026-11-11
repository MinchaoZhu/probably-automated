using System.Linq;
using System.Collections.Generic;

namespace AutoMod.Domain.Rules;

/// <summary>效果的种类。</summary>
public static class EffectKind
{
    public const string Moved = "moved";
    public const string Created = "created";
    public const string Removed = "removed";
    public const string Changed = "changed";
}

/// <summary>一次操作对世界造成的一项变化。</summary>
public class Effect
{
    public string Kind = "";
    public int Uid;
    public string Item = "";

    /// <summary>移动：从哪个容器到哪个容器（uid，0 = 顶层 / 不存在）；生成：所在容器。</summary>
    public int From, To;

    /// <summary>变化：属性与新旧值。</summary>
    public string Prop;
    public double? Old, New;
}

/// <summary>
/// 世界端口：解释器只通过它读写世界。游戏实现 GameWorld，模拟器实现 SimWorld。
/// 物品用 object 表示；同一件物品多次取到时 Key 相等。命令只有放置、更换、作用、标注四种机制。
/// 作用的能否和效果都由世界决定：游戏世界问游戏；模拟世界不知道效果（只计 AP，物品不变）。
/// </summary>
public interface IWorld
{
    int Day { get; }

    // ---------- 查询 ----------

    /// <summary>
    /// 全店：店里归玩家的东西（任意层），不含待售的（柜台、陈列柜、售货机里的货）、垃圾和文件。
    /// </summary>
    IReadOnlyList<object> Shop();

    /// <summary>起了这个设备名的东西（Places）。</summary>
    IReadOnlyList<object> Named(string name);

    /// <summary>东西的设备名；没起为空。</summary>
    string DeviceName(object item);

    /// <summary>某隔间的直接内容物；role 为空 = 主空间（battery / filter = 电池槽 / 滤芯槽）。</summary>
    IReadOnlyList<object> Contents(object box, string role);

    /// <summary>直接装着它的容器；顶层或已不存在时为空。</summary>
    object Parent(object item);

    bool Exists(object item);
    object Key(object item);
    int Uid(object item);

    /// <summary>种类（游戏原型 id）。</summary>
    string Kind(object item);

    /// <summary>直接类别（不含上级类别，由 Catalog.Closure 补全）。</summary>
    IReadOnlyCollection<string> Traits(object item);

    /// <summary>种类档案：见过的该种类物品的类别；没见过为空。</summary>
    IReadOnlyCollection<string> KindTraits(string kind);

    /// <summary>数值属性（布尔取 0 / 1），容器另有 count / free / cap；不存在或无法计算时为空。派生属性由目录计算。</summary>
    double? Num(object item, string prop);

    /// <summary>文字属性（sex 等）。</summary>
    string Str(object item, string prop);

    /// <summary>能否放入容器的主空间。</summary>
    bool Fits(object box, object item, string slot = null);

    string Describe(object item);

    /// <summary>店里存放着的物品（直接放在存放类容器里，例如工具箱、储藏区；柜台上待售的不算），按 uid 排序。工具自动查找用。</summary>
    IReadOnlyList<object> Stock();

    // ---------- 命令 ----------

    /// <summary>放置：放进容器的主空间。</summary>
    StepResult Place(object item, object box, string slot = null);

    /// <summary>交换：a 和 b 互换位置（包括所在的槽位）。</summary>
    StepResult Swap(object a, object b);

    /// <summary>作用前询问：工具现在能否作用于对象（游戏：CanTarget 且 MayTarget）。</summary>
    bool CanApply(object tool, object target);

    /// <summary>
    /// 作用一次。新出现的物品放进 outputs 中第一个可容纳的容器（为空 = 留在游戏放的位置）。
    /// 失败码：NoEffect = 允许但什么都没变；NeedsPlayer = 游戏要打开界面让玩家操作（世界自己记住，之后 CanApply 为假）。
    /// </summary>
    StepResult Apply(object tool, object target, IReadOnlyList<object> outputs);

    /// <summary>店里的固定设施（水龙头、下水道、垃圾桶：不在任何容器里）；没有返回 null。name 见 Fixtures。</summary>
    object Fixture(string name);

    /// <summary>容器（机器）窗口里的格子：(id, 名称)，id 形如 "slot:seed"，可作挑选器的“取自”。没有分格的为空。</summary>
    IReadOnlyList<(string id, string name)> Slots(object box);

    /// <summary>对象窗口里的按钮（显示文字）；没有按钮的为空。</summary>
    IReadOnlyList<string> Buttons(object item);

    /// <summary>触发前询问：对象现在能否这样触发（how：Triggers.Activate / Toggle / Button(文字)）。</summary>
    bool CanTrigger(object item, string how);

    /// <summary>触发一次；产物、失败码同 Apply。</summary>
    StepResult Trigger(object item, string how, IReadOnlyList<object> outputs);

    /// <summary>标注：添加 / 移除 mod 标记。</summary>
    StepResult Annotate(object item, string text, bool on);
}

/// <summary>触发方式（IWorld.CanTrigger / Trigger 的 how）。</summary>
public static class Triggers
{
    public const string Activate = "activate";
    public const string Toggle = "toggle";
    public const string ButtonPrefix = "button:";

    public static string Button(string label) => ButtonPrefix + label;

    /// <summary>动作对应的触发方式。</summary>
    public static string Of(ActionDef a) => a.Verb switch
    {
        ActVerb.Toggle => Toggle,
        ActVerb.Press => Button(a.Button ?? ""),
        _ => Activate,
    };
}

/// <summary>
/// 固定设施：店里本来就有、不在任何容器里的东西（水龙头、下水道、垃圾桶），可以直接写这些 id。
/// 都能当工具来源；垃圾桶是容器，还能当放入的目标、筛选器的范围（用法和设备名一样，不用命名，读档也不会丢）。
/// </summary>
public static class Fixtures
{
    public const string Faucet = "@faucet";
    public const string Drain = "@drain";
    public const string Trash = "@trash";

    public static (string id, string name)[] All => new[]
    {
        (Faucet, Name(Faucet)),
        (Drain, Name(Drain)),
        (Trash, Name(Trash)),
    };

    /// <summary>容器设施（垃圾桶）。</summary>
    public static (string id, string name)[] Boxes => new[] { (Trash, Name(Trash)) };

    public static bool Is(string id) => id != null && (id == Faucet || id == Drain || id == Trash);

    /// <summary>是容器的设施：和设备名一样当容器用。</summary>
    public static bool IsBox(string id) => id == Trash;

    public static string Name(string id) => id switch
    {
        Faucet => L.T("fixture.faucet"),
        Drain => L.T("fixture.drain"),
        Trash => L.T("fixture.trash"),
        _ => null,
    };
}

/// <summary>
/// 筛选器的来源、动作的目标除了筛选器以外的两种：全店（“@shop”）和设备名（“#满电区”）。
/// 设备名是玩家在店里给东西起的名字，记在东西身上；几个东西可以叫同一个名字。
/// </summary>
public static class Places
{
    public const string Shop = "@shop";
    public const string NamePrefix = "#";

    public static bool IsShop(string id) => id == Shop;
    public static bool IsName(string id) => id != null && id.Length > 1 && id.StartsWith(NamePrefix);
    public static string NameOf(string id) => IsName(id) ? id[NamePrefix.Length..] : null;
    public static string Ref(string name) => NamePrefix + name;

    /// <summary>当容器用的设备：设备名，或垃圾桶这类容器设施。东西见 Eval.Device。</summary>
    public static bool IsDevice(string id) => IsName(id) || Fixtures.IsBox(id);

    /// <summary>不是定义的引用（全店、设备名、水龙头这类固定设施）。</summary>
    public static bool IsBuiltin(string id) => IsShop(id) || IsName(id) || Fixtures.Is(id);
}

/// <summary>机器槽位（面板角色）的中文名：游戏读不到标题时用；没列的显示角色名。</summary>
public static class SlotNames
{
    public const string Prefix = "slot:";

    /// <summary>储藏区的第一个标签页。未扩建的储藏区只有这一页，整个网格都算它。</summary>
    public const string FirstTab = "storage#1";

    /// <summary>
    /// 储藏区标签页的角色：玩家改过名的用名字（"storage@肉类"，几个储藏区的同名标签页算同一个槽位），
    /// 没改过的用序号（"storage#2"）。
    /// </summary>
    public static string TabRole(string panel, int index, string custom) =>
        string.IsNullOrEmpty(custom) ? $"{panel}#{index + 1}" : $"{panel}@{custom}";

    public static bool IsTab(string id) => id != null && (id.Contains('@') || id.Contains('#'));

    /// <summary>这个槽位是不是 panel（"slot:storage"）里的一个标签页。</summary>
    public static bool IsTabOf(string id, string panel) =>
        id != null && panel != null && (id.StartsWith(panel + "@") || id.StartsWith(panel + "#"));

    /// <summary>店里读不到标题时标签页的名字：改过名的就是名字，没改过的是面板名加序号。</summary>
    private static string TabName(string role)
    {
        int at = role.IndexOf('@');
        if (at >= 0) return role[(at + 1)..];
        int hash = role.IndexOf('#');
        return hash >= 0 ? L.T("slot." + role[..hash]) + " " + role[(hash + 1)..] : null;
    }

    /// <summary>
    /// 游戏里读到的格子标题（适配器读窗口时记下），按“机器种类|角色”存：不同机器的同名角色标题可以不同
    /// （水培机的 seed 是“种子输入”，DNA 编辑器的 seed 是“目标”）。
    /// </summary>
    private static readonly Dictionary<string, string> Learned = new();

    public static void ClearLearned() => Learned.Clear();

    /// <summary>起了某个设备名的东西的种类：适配器设置，用来按机器种类取槽位标题。</summary>
    public static System.Func<string, IEnumerable<string>> NamedKinds;

    public static void Learn(string kind, string role, string label)
    {
        if (!string.IsNullOrEmpty(role) && !string.IsNullOrEmpty(label)) Learned[(kind ?? "") + "|" + role] = label;
    }

    /// <summary>
    /// 格子的名字。给了机器种类时用这些种类读到的标题（只有一种时）；没给时，各机器读到的标题一致才用；
    /// 否则查本地化表 slot.<role>。
    /// </summary>
    public static string Name(string id, IEnumerable<string> kinds = null)
    {
        if (id == null) return null;
        string role = id.StartsWith(Prefix) ? id[Prefix.Length..] : id;
        var want = kinds == null ? null : new HashSet<string>(kinds);
        var labels = new HashSet<string>();
        foreach (var kv in Learned)
        {
            int bar = kv.Key.LastIndexOf('|');
            if (kv.Key[(bar + 1)..] != role) continue;
            if (want != null && want.Count > 0 && !want.Contains(kv.Key[..bar])) continue;
            labels.Add(kv.Value);
        }
        if (labels.Count == 1) return labels.First();
        return TabName(role) ?? L.T("slot." + role);
    }

    /// <summary>槽位在这些设备名 / 筛选器所指机器上的名字。</summary>
    public static string Name(string id, RulesData d, IEnumerable<string> refs) => Name(id, KindsOf(d, refs));

    /// <summary>设备名 / 筛选器引用指向的机器种类（设备名按起了这个名字的东西，筛选器按类型或来源往上找）。</summary>
    public static HashSet<string> KindsOf(RulesData d, IEnumerable<string> refs, int depth = 0)
    {
        var kinds = new HashSet<string>();
        if (d == null || refs == null || depth > 8) return kinds;
        foreach (var r in refs)
        {
            if (string.IsNullOrEmpty(r)) continue;
            if (Places.IsName(r))
            {
                if (NamedKinds != null)
                    foreach (var k in NamedKinds(Places.NameOf(r)))
                        if (!string.IsNullOrEmpty(k)) kinds.Add(k);
                continue;
            }
            var s = d.Selectors.FirstOrDefault(x => x.Id == r);
            if (s == null) continue;
            if (!string.IsNullOrEmpty(s.Kind) && s.Kind != Catalog.Any && !Catalog.IsTrait(s.Kind)) kinds.Add(s.Kind);
            else kinds.UnionWith(KindsOf(d, s.From, depth + 1));
        }
        return kinds;
    }
}
