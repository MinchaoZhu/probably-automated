using System.Collections.Generic;
using AutoMod.Domain.Reports;

namespace AutoMod.Domain.Rules;

/// <summary>结构化的单项属性变化。</summary>
public class StepChange
{
    public string Name { get; set; } = "";
    public string Old { get; set; } = "";
    public string New { get; set; } = "";

    public StepChange() { }
    public StepChange(string name, string oldVal, string newVal)
    {
        Name = name;
        Old = oldVal;
        New = newVal;
    }
}

public class StepResult
{
    public bool Ok;
    public string Text = "";     // 做了什么（成功时）
    public string Reason = "";   // 为什么没做成（失败时）
    public string Code;          // 失败原因分类（FailCode）
    public string DestWs;        // 放不下时：哪个工位满了

    /// <summary>排班引擎：这一步对世界造成的变化。</summary>
    public List<AutoMod.Domain.Rules.Effect> Effects = new();

    // 结构化变化数据
    public string Label;
    public List<StepChange> Changes = new();
    public List<string> OtherParts = new();

    public static StepResult Done(string text) => new() { Ok = true, Text = text };
    public static StepResult Fail(string reason, string code = FailCode.NotAllowed) => new() { Ok = false, Reason = reason, Code = code };
}
