using System.Collections.Generic;
using System.Linq;
using AutoMod.Adapter;
using AutoMod.Domain.Rules;

namespace AutoMod.App;

/// <summary>新建示例排班（不用设置，全店找）、排班的导入导出、设备名。</summary>
public static class ExampleSetup
{
    public static Schedule BatteryRotation()
    {
        var s = Examples.BatteryRotation(Session.D);
        Session.Changed();
        return s;
    }

    public static Schedule Profession(string id)
    {
        var s = ProfessionExamples.Build(Session.D, id);
        Session.Changed();
        return s;
    }

    /// <summary>导出排班（剪贴板用的一段文字）。</summary>
    public static string Export(Schedule s) => Transfer.Export(Session.D, s, ModInfo.Version);

    /// <summary>从一段文字导入排班；不是排班时抛 TransferException。</summary>
    public static Schedule Import(string text)
    {
        var s = Transfer.Import(Session.D, text);
        Session.InvalidateWorld();
        Session.Changed();
        return s;
    }

    // ---------- 设备名 ----------

    /// <summary>设备名展开成筛选器（这个排班里直接用它当物品列表的地方换成新筛选器）。</summary>
    public static SelectorDef ExpandName(Schedule s, string name)
    {
        var sel = Defs.ExpandName(Session.D, s, name);
        Session.Changed();
        return sel;
    }

    /// <summary>设备名转为本流程的设备参数（这个流程里引用它的地方都改成引用参数）。</summary>
    public static ObjectDef DeviceToParam(Schedule s, string name)
    {
        var p = Defs.DeviceToParam(Session.D, s, name);
        Session.Changed();
        return p;
    }

    /// <summary>店里已经起了的设备名（去重、排序；不含游戏给料斗自动编的“#1”“#2”）。</summary>
    public static List<string> DeviceNames() =>
        GameApi.CollectAll().Select(GameApi.DeviceName).Where(n => n != null && !IsAutoNumber(n)).Distinct().OrderBy(n => n).ToList();

    /// <summary>游戏自动编的号（料斗放下时叫“#1”“#2”……）。</summary>
    private static bool IsAutoNumber(string n) => n.Length > 1 && n[0] == '#' && n.Skip(1).All(char.IsDigit);

    /// <summary>所有排班里用到、但店里还没有东西叫这个名字的设备名。</summary>
    public static List<string> WantedNames() =>
        Check.MissingNames(Session.D, Session.D.Schedules.SelectMany(s => s.Rules).SelectMany(Check.RuleRefs), Session.World);

    /// <summary>排班用到、但店里还没有东西叫这个名字的设备名。</summary>
    public static List<string> MissingNames(Schedule s) =>
        Check.MissingNames(Session.D, s.Rules.SelectMany(Check.RuleRefs), Session.World);

    /// <summary>给店里的东西起设备名（为空 = 去掉）。</summary>
    public static void SetDeviceName(GameItem item, string name)
    {
        GameApi.SetDeviceName(item, name);
        Session.InvalidateWorld();
        Session.Changed();
    }
}
