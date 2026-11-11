using System.Collections.Generic;
using AutoMod.Domain.Workforce;

namespace AutoMod.Domain.Rules;

// 排班与规则的数据模型。全部是纯数据，序列化成 JSON 存进存档。
// 引用一律按 Id；名称只用于显示。

/// <summary>
/// 定义 + 排班。参数、筛选器可以属于某个排班（Owner），见 Scope。店里的东西不在这里登记：筛选器从全店找，
/// 或者按设备名（玩家在店里给东西起的名字，记在东西身上）找，见 Places。
/// </summary>
public class RulesData
{
    public List<ObjectDef> Objects { get; set; } = new();
    public List<PropertyDef> Properties { get; set; } = new();

    /// <summary>顺序即拓扑序：每个挑选器只能引用排在它上方的挑选器。全局的在前，本排班的在后（Scope.Normalize）。</summary>
    public List<SelectorDef> Selectors { get; set; } = new();

    public List<Schedule> Schedules { get; set; } = new();
}

/// <summary>参数的类型：数值（默认）、设备（值是一个设备名）、列表（值是一个筛选器）。</summary>
public static class ParamType
{
    public const string Number = null;
    public const string Device = "device";
    public const string List = "list";
}

/// <summary>
/// 参数：有名字的值，排班卡片上直接改。数值（换电阈值 30%）在条件、数量里引用；设备（写入机 =【芦苇写入机】）、
/// 列表（目标基因盘 = 一张净化水质盘）在任务、筛选器里当设备名 / 筛选器用。流程被别的流程执行时，本流程参数可以由调用方覆盖。
/// </summary>
public class ObjectDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>类型：为空 = 数值；ParamType.Device / List。</summary>
    public string Type { get; set; }

    /// <summary>设备、列表参数的值：设备名引用（"#名字"）或筛选器 Id。</summary>
    public string Ref { get; set; }

    /// <summary>设备或列表参数（值是引用，不是数）。</summary>
    [Newtonsoft.Json.JsonIgnore] public bool IsRef => Type is ParamType.Device or ParamType.List;

    /// <summary>所属排班的 Id；为空 = 共用。</summary>
    public string Owner { get; set; }

    public double Value { get; set; }

    /// <summary>单位（g、只、天、%），排班卡片上显示在数值后面；可为空。</summary>
    public string Unit { get; set; }

    /// <summary>说明，排班卡片上悬停名字时显示；可为空。</summary>
    public string Hint { get; set; }
}

/// <summary>自定义属性：给某种物品加一个按公式计算的数值属性。公式只能用自身属性和常量。</summary>
public class PropertyDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>适用的物品类型（物品类型或种类组）。</summary>
    public string ItemKind { get; set; } = "rat";

    public Expr Formula { get; set; } = new();
}

public static class SelectorMode
{
    public const string Items = "items";
    public const string Number = "number";
}

/// <summary>
/// 挑选器：来源 → 展开 → 类型 → 条件 → 排除 → 排序 + 截取（结果为对象列表）或统计（结果为数值）。
/// </summary>
public class SelectorDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Mode { get; set; } = SelectorMode.Items;

    /// <summary>所属排班的 Id；为空 = 全局。</summary>
    public string Owner { get; set; }

    /// <summary>来源：对象 Id 或（上方）挑选器 Id。</summary>
    public List<string> From { get; set; } = new();

    /// <summary>范围：为空 = 全部内容物；"battery" / "filter" = 该槽位里的物品。</summary>
    public string Slot { get; set; }

    /// <summary>类型：物品类型（rat）、种类组（group:battery）、容器类型（box:cage）、any（全部物品）、self（来源容器本身）。可用 | 并列多个。</summary>
    public string Kind { get; set; } = "any";

    public CondGroup Where { get; set; } = new();
    public string Minus { get; set; }
    public SortSpec Sort { get; set; }
    public TakeSpec Take { get; set; }
    public AggSpec Agg { get; set; }

    public bool IsNumber => Mode == SelectorMode.Number;
}

public class SortSpec
{
    public string Prop { get; set; } = "";
    public bool Desc { get; set; } = true;
}

public static class TakeMode
{
    public const string First = "first"; // 前 N 个
    public const string From = "from";   // 第 N 个起
}

public class TakeSpec
{
    public string Mode { get; set; } = TakeMode.First;
    public Expr N { get; set; } = Expr.Of(1);
}

public static class AggFn
{
    public const string Count = "count";
    public const string Min = "min";
    public const string Max = "max";
    public const string Avg = "avg";
    public const string Sum = "sum";
}

public class AggSpec
{
    public string Fn { get; set; } = AggFn.Count;
    public string Prop { get; set; } = "";
}

/// <summary>条件组：满足全部 / 满足任一。</summary>
public class CondGroup
{
    public string Join { get; set; } = "all";
    public List<Cond> Items { get; set; } = new();

    public bool IsAny => Join == "any";
}

/// <summary>条件：预设条件、属性比较（右侧为表达式），或一层子条件组。三者取其一。</summary>
public class Cond
{
    public string Preset { get; set; }
    public string Prop { get; set; }
    public string Op { get; set; } = ">=";
    public Expr Right { get; set; }

    /// <summary>文字属性比较（性别 = MALE）：不为空时按文字相等判断，Op 为 = 或 !=。</summary>
    public string Text { get; set; }

    public CondGroup Group { get; set; }

    public static Cond Of(string preset) => new() { Preset = preset };
    public static Cond Cmp(string prop, string op, Expr right) => new() { Prop = prop, Op = op, Right = right };
    public static Cond Sub(string join, params Cond[] items) => new() { Group = new CondGroup { Join = join, Items = new List<Cond>(items) } };
}

public static class TermKind
{
    public const string Num = "num";     // 数值
    public const string Const = "const"; // 常量（对象）
    public const string Own = "own";     // 自身属性
    public const string Stat = "stat";   // 挑选器的统计值（列表取数量）
    public const string Note = "note";   // 记事本
    public const string Sys = "sys";     // 系统变量
}

public static class SysVar
{
    public const string ApLeft = "apLeft";
    public const string Shift = "shift";
    public const string Day = "day";
}

/// <summary>表达式：项 (运算 项)*，运算为 + - * /，先乘除后加减，无括号。</summary>
public class Expr
{
    public List<Term> Terms { get; set; } = new();
    public List<string> Ops { get; set; } = new();

    public static Expr Of(double n) => new() { Terms = { new Term { Kind = TermKind.Num, Num = n } } };
    public static Expr Ref(string kind, string id) => new() { Terms = { new Term { Kind = kind, Ref = id } } };

    public Expr Then(string op, Term t)
    {
        Ops.Add(op);
        Terms.Add(t);
        return this;
    }
}

public class Term
{
    public string Kind { get; set; } = TermKind.Num;
    public double Num { get; set; }
    public string Ref { get; set; }

    public static Term N(double n) => new() { Kind = TermKind.Num, Num = n };
    public static Term C(string id) => new() { Kind = TermKind.Const, Ref = id };
    public static Term Own(string prop) => new() { Kind = TermKind.Own, Ref = prop };
    public static Term Stat(string id) => new() { Kind = TermKind.Stat, Ref = id };
    public static Term Note(string id) => new() { Kind = TermKind.Note, Ref = id };
    public static Term Sys(string id) => new() { Kind = TermKind.Sys, Ref = id };
}

/// <summary>排班：按顺序执行的规则列表 + 记事本。独立于学徒存在。</summary>
public class Schedule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>说明：玩家写的一段话（做什么、要准备什么）。流程页悬停名称和“详情”里显示；可为空。</summary>
    public string Description { get; set; }

    public List<NoteDef> Notepad { get; set; } = new();
    public List<WorkRule> Rules { get; set; } = new();

    /// <summary>建立时间（UTC ticks）：选排班的下拉框里新建的排在最前。0 = 旧数据。</summary>
    public long Created { get; set; }

    /// <summary>工作构成（隐藏）：按实际花的 AP 统计、滑动平均，没执行过时为空。</summary>
    public Dictionary<string, double> Profile { get; set; }
}

public class NoteDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Init { get; set; }
}

/// <summary>规则：执行前提 + 执行动作序列。</summary>
public class WorkRule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Join { get; set; } = "all";
    public List<PreCond> Pre { get; set; } = new();
    public List<ActionDef> Actions { get; set; } = new();
}

public static class PreRef
{
    public const string Selector = "sel";
    public const string Note = "note";
    public const string Sys = "sys";
}

public static class PreTest
{
    public const string NotEmpty = "notEmpty";
    public const string Empty = "empty";
    public const string CountGt = "cnt>";
    public const string CountLt = "cnt<";
    // 数值比较：> >= < <= =
}

public class PreCond
{
    public string RefKind { get; set; } = PreRef.Selector;
    public string Ref { get; set; } = "";
    public string Test { get; set; } = PreTest.NotEmpty;
    public Expr Right { get; set; } = Expr.Of(0);

    /// <summary>列表前提的条件（可空）：只数满足它的（“有 含水 &gt; 1000 的【A】”）。</summary>
    public Cond Where { get; set; }
}

public static class ActVerb
{
    public const string Move = "move";
    public const string Swap = "swap";

    /// <summary>使用：用一件工具作用于对象（游戏里“拿 A 点 B”）。能否作用和效果都由游戏决定。</summary>
    public const string Use = "use";

    /// <summary>激活 / 开关：对象自己的交互（游戏里双击使用、开关），不用工具。能否执行和效果都由游戏决定。</summary>
    public const string Activate = "activate";
    public const string Toggle = "toggle";

    /// <summary>按按钮：按下机器窗口里的一个按钮（不打开窗口）。按钮按显示文字认。</summary>
    public const string Press = "press";
    public const string Mark = "mark";
    public const string Unmark = "unmark";
    public const string Note = "note";

    /// <summary>执行流程：把另一个流程的任务按顺序做一遍（子流程），可以覆盖它的子流程参数。</summary>
    public const string Call = "call";

    public static readonly string[] All = { Move, Swap, Use, Activate, Toggle, Press, Mark, Unmark, Note, Call };
}

/// <summary>
/// 动作：只说做什么。对哪些、按什么顺序、几件、好不好，都由筛选器（有序集合）决定。
/// 放入：List 逐件放进 To 里第一个放得下的；交换：List 和 To 按顺序一一配对互换位置；
/// 使用：To 的每一件按顺序试 Tool 里的东西，方向由游戏判断（Tool 作用于对象不行就反过来）。
/// </summary>
public class ActionDef
{
    public string Verb { get; set; } = ActVerb.Move;

    /// <summary>对象（集合）：被放入、被交换、被作用、被按的东西。</summary>
    public string List { get; set; }

    /// <summary>放入：目标容器（集合，按顺序试）；交换：和 List 配对的另一组东西。</summary>
    public string To { get; set; }

    /// <summary>放入：目标的槽位（"slot:seed"、储藏区的标签页）；为空 = 由游戏选择。</summary>
    public string ToSlot { get; set; }

    /// <summary>使用：工具（集合）：设备名、筛选器、水龙头这类固定设施。</summary>
    public string Tool { get; set; }

    /// <summary>使用、激活、按按钮：每件对象几次。</summary>
    public int Times { get; set; } = 1;

    /// <summary>按按钮：按钮的显示文字（图标按钮是图标名）。</summary>
    public string Button { get; set; }

    // 记录
    public string Note { get; set; }
    public string NoteMode { get; set; } = "add";
    public Expr Value { get; set; }

    /// <summary>标记名称。</summary>
    public string Text { get; set; }

    /// <summary>执行流程：被调用的流程 Id。</summary>
    public string Flow { get; set; }

    /// <summary>执行流程：覆盖的子流程参数（没列的用默认值）。</summary>
    public List<CallArg> Args { get; set; }
}

/// <summary>调用时覆盖被调用流程的一个本流程参数：设备、列表换成调用方的设备名、筛选器或参数（Ref），数值换成 Num。</summary>
public class CallArg
{
    /// <summary>被调用流程的本流程参数 Id。</summary>
    public string Param { get; set; } = "";

    public string Ref { get; set; }
    public double? Num { get; set; }
}
