namespace AutoMod.Domain.Workforce;

/// <summary>班次：早班 / 晚班。</summary>
public static class Shift
{
    public const string Morning = "morning";
    public const string Night = "night";
    public const string Both = "both";

    public static bool Matches(string ruleShift, string current) =>
        ruleShift == Both || ruleShift == current;

    public static string DisplayName(string s) => L.T("shift." + s);
}
