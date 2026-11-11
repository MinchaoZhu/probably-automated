using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AutoMod.Domain;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Simulation;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Workforce;

/// <summary>
/// 多语言基础设施测试：
/// 读 CSV、L 的查表/复数/模板/连接、键格式、占位符一致性、代码键覆盖。
/// </summary>
internal static class LocTests
{
    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "ProbablyStolenMod.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("找不到仓库根目录");
    }

    private static string LocDir => Path.Combine(FindRoot(), "src", "AutoMod", "ModFiles", "Localization");

    public static Dictionary<string, string> LoadTable(string locale)
    {
        string path = Path.Combine(LocDir, $"{locale}.csv");
        return File.Exists(path) ? L.ParseCsv(File.ReadAllLines(path)) : new Dictionary<string, string>();
    }

    private static string FitText(string text, float width, int size, out bool cut)
    {
        float W(char c) => c > 0x2E7F ? size : size * 0.56f;
        float total = text.Sum(W);
        cut = total > width;
        if (!cut) return text;
        float room = width - size, used = 0;
        int n = 0;
        while (n < text.Length && used + W(text[n]) <= room) used += W(text[n++]);
        return text.Substring(0, n) + "…";
    }

    private static float EstWidth(string text, int size = 13)
    {
        float w = 0;
        foreach (char c in text) w += c > 0x2E7F ? size : size * 0.56f;
        return w;
    }

    public static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("多语言·L 查表与后备：当前语言 → 英文 → 键本身，记入 Missing", () =>
        {
            var cur = new Dictionary<string, string> { ["btn.ok"] = "确定", ["btn.cancel"] = "取消" };
            var en = new Dictionary<string, string> { ["btn.ok"] = "OK", ["btn.cancel"] = "Cancel", ["btn.retry"] = "Retry" };
            L.Use("zh", cur, en);

            check(L.T("btn.ok") == "确定", "取当前语言");
            check(L.T("btn.retry") == "Retry", "当前语言缺失退到英文后备");
            check(L.Missing.Contains("btn.retry"), "退到英文时记入 Missing");

            check(L.T("btn.unknown") == "btn.unknown", "两表均缺失返回键本身");
            check(L.Missing.Contains("btn.unknown"), "缺失记入 Missing");

            check(L.T("format.sample", "A", 1) == "format.sample", "格式化失败返回键本身");

            var formatTable = new Dictionary<string, string> { ["msg.hello"] = "你好，{0}！" };
            L.Use("zh", formatTable, null);
            check(L.T("msg.hello", "世界") == "你好，世界！", "参数正确格式化");
        });

        test("多语言·复数规则：8 种语言的复数判定与 L.N 格式化", () =>
        {
            check(L.PluralIndex("zh", 1) == 0 && L.PluralIndex("zh", 5) == 0, "zh 只有单种形式");
            check(L.PluralIndex("ja", 1) == 0 && L.PluralIndex("ja", 5) == 0, "ja 只有单种形式");

            check(L.PluralIndex("en", 1) == 0 && L.PluralIndex("en", 2) == 1 && L.PluralIndex("en", 0) == 1, "en 1 为 0，其余为 1");
            check(L.PluralIndex("de", 1) == 0 && L.PluralIndex("de", 2) == 1, "de 1 为 0，其余为 1");
            check(L.PluralIndex("es", 1) == 0 && L.PluralIndex("es", 2) == 1, "es 1 为 0，其余为 1");

            check(L.PluralIndex("fr", 0) == 0 && L.PluralIndex("fr", 1) == 0 && L.PluralIndex("fr", 2) == 1, "fr 0 和 1 为 0，其余为 1");

            // ru: one (1, 21...), few (2-4, 22-24...), many (0, 5-20, 25...)
            check(L.PluralIndex("ru", 1) == 0 && L.PluralIndex("ru", 21) == 0, "ru one");
            check(L.PluralIndex("ru", 2) == 1 && L.PluralIndex("ru", 4) == 1 && L.PluralIndex("ru", 24) == 1, "ru few");
            check(L.PluralIndex("ru", 5) == 2 && L.PluralIndex("ru", 11) == 2 && L.PluralIndex("ru", 20) == 2, "ru many");

            // pl: 1 为 0，2-4 为 1，5+ 为 2
            check(L.PluralIndex("pl", 1) == 0, "pl one");
            check(L.PluralIndex("pl", 2) == 1 && L.PluralIndex("pl", 4) == 1 && L.PluralIndex("pl", 22) == 1, "pl few");
            check(L.PluralIndex("pl", 5) == 2 && L.PluralIndex("pl", 11) == 2, "pl many");

            var pluralTable = new Dictionary<string, string>
            {
                ["item.count"] = "{0} 件|{0} 件",
                ["item.count.en"] = "{0} item|{0} items",
            };
            L.Use("en", pluralTable, pluralTable);
            check(L.N("item.count.en", 1) == "1 item", "en 单数");
            check(L.N("item.count.en", 5) == "5 items", "en 复数");
            check(L.NAs("item.count.en", 5, "N") == "N items", "en NAs 表达式显示");
        });

        test("多语言·列表连接与卡片模板：Join / Template", () =>
        {
            var cur = new Dictionary<string, string>
            {
                ["card.move"] = "将 {list} 放入 {to}",
                ["conj.and"] = "且",
            };
            var en = new Dictionary<string, string>
            {
                ["card.move"] = "Move {list} to {to}",
                ["conj.and"] = "and",
            };
            var ja = new Dictionary<string, string>
            {
                ["card.move"] = "{to}に{list}を移動",
                ["conj.and"] = "かつ",
            };
            L.Use("zh", cur, en);

            check(L.Join("list", new[] { "A", "B", "C" }) == "A、B、C", "中文 list 顿号连接");
            check(L.Join("and", new[] { "A", "B" }) == "A 且 B", "中文 and 连接");

            L.Use("ja", ja, en);
            check(L.Join("list", new[] { "A", "B", "C" }) == "A、B、C", "日文 list 顿号连接");
            check(L.Join("and", new[] { "A", "B" }) == "A かつ B", "日文 and 条件连接");

            L.Use("en", en, en);
            check(L.Join("list", new[] { "A", "B", "C" }) == "A, B, C", "英文 list 逗号连接");
            check(L.Join("and", new[] { "A", "B" }) == "A and B", "英文 and 连接");

            var tplZh = L.Template("card.move");
            check(tplZh.Count == 4 && tplZh[1].slot == "list" && tplZh[3].slot == "to", "模板解析槽位 zh");

            L.Use("en", en, en);
            var tplEn = L.Template("card.move");
            check(tplEn.Count == 4 && tplEn[1].slot == "list" && tplEn[3].slot == "to", "模板解析槽位 en");
        });

        test("多语言·CSV 解析符合游戏规范：注释/空行/引号/首尾空白/转义换行", () =>
        {
            var lines = new[]
            {
                "# 这是注释",
                "",
                "basic.key,Hello World",
                "  trim.key  ,  value with spaces  ",
                "quoted.key,\"value with \"\"inner quotes\"\"\"",
                "multiline.key,\"line1\\nline2\"",
                "comma.in.value,first,second,third",
            };
            var table = L.ParseCsv(lines);
            check(table["basic.key"] == "Hello World", "基本键值");
            check(table["trim.key"] == "value with spaces", "键和值去除首尾空白");
            check(table["quoted.key"] == "value with \"inner quotes\"", "双引号与转义双引号还原");
            check(table["multiline.key"] == "line1\nline2", "\\n 转义为换行");
            check(table["comma.in.value"] == "first,second,third", "只按第一个逗号分隔");
            check(table.Count == 5, "注释与空行已忽略");
        });

        test("多语言·翻译表格式与占位符规范：无中文逗号/键名合法/占位符匹配", () =>
        {
            string locDir = LocDir;
            if (!Directory.Exists(locDir)) return;

            var keyRegex = new Regex(@"^[a-z0-9_.]+$");
            var phRegex = new Regex(@"\{\w+\}");

            var files = Directory.GetFiles(locDir, "*.csv");
            check(files.Length >= 8, $"应包含 8 种语言表（当前 {files.Length} 个）");

            Dictionary<string, string> enTable = null;
            if (File.Exists(Path.Combine(locDir, "en.csv")))
                enTable = L.ParseCsv(File.ReadAllLines(Path.Combine(locDir, "en.csv")));

            foreach (var f in files)
            {
                string lang = Path.GetFileNameWithoutExtension(f);
                var lines = File.ReadAllLines(f);
                var table = L.ParseCsv(lines);

                if (enTable != null)
                {
                    check(enTable.Keys.SequenceEqual(table.Keys), $"[{lang}] 键顺序与 en.csv 一致");
                    var missingInLang = enTable.Keys.Where(k => !table.ContainsKey(k)).ToList();
                    check(missingInLang.Count == 0, $"[{lang}] 缺少键：{string.Join(", ", missingInLang.Take(5))}");
                }

                foreach (var (k, v) in table)
                {
                    check(keyRegex.IsMatch(k), $"[{lang}] 键名必须为 [a-z0-9_.]：[{k}]");
                    check(!k.Contains(','), $"[{lang}] 键名不能含英文逗号：[{k}]");
                    check(k == k.Trim(), $"[{lang}] 键名无首尾空格：[{k}]");

                    if (enTable != null && enTable.TryGetValue(k, out var enVal))
                    {
                        var enPlaceholders = phRegex.Matches(enVal).Select(m => m.Value).ToHashSet();
                        foreach (var form in v.Split('|'))
                        {
                            var formPlaceholders = phRegex.Matches(form).Select(m => m.Value).ToHashSet();
                            // 复数形式如果省去了数量占位符如 {0}（例如单数写法 "one item"），允许差 {0}
                            bool match = formPlaceholders.SetEquals(enPlaceholders) ||
                                         (v.Contains('|') && formPlaceholders.SetEquals(enPlaceholders.Where(x => x != "{0}")));
                            check(match, $"[{lang}] 键 [{k}] 占位符与 en.csv 不一致：[{form}] vs [{enVal}]");
                        }
                    }
                }
            }

            // 测试结束后恢复正常语言表
            L.Use("zh", LoadTable("zh"), LoadTable("en"));
        });

        test("多语言·复数规则调用检查与生成文本不含竖线", () =>
        {
            var en = LoadTable("en");
            var pluralKeys = en.Where(kv => kv.Value.Contains('|')).Select(kv => kv.Key).ToHashSet();
            check(pluralKeys.Count > 0, $"en.csv 中应有复数键（当前找到 {pluralKeys.Count} 个）");

            // 1. 扫描源码：所有复数键只能经 L.N / L.NAs 读取，禁止通过 L.T 读取
            string srcRoot = Path.Combine(FindRoot(), "src");
            var violations = new List<string>();
            var ltRegex = new Regex(@"L\.T\(\s*""([^""]+)""");
            foreach (var f in Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(f);
                foreach (Match m in ltRegex.Matches(text))
                {
                    string key = m.Groups[1].Value;
                    if (pluralKeys.Contains(key))
                        violations.Add($"{Path.GetFileName(f)}: L.T(\"{key}\")");
                }
            }
            check(violations.Count == 0, $"复数键不能用 L.T 读取（必须使用 L.N）：\n" + string.Join("\n", violations));

            // 三元表达式选的键上面的正则看不到，单独查表里有没有
            var ternary = new Regex(@"L\.T\([^""\n]*\?\s*""([a-z0-9_.]+)""\s*:\s*""([a-z0-9_.]+)""");
            var missing = new List<string>();
            foreach (var f in Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
                foreach (Match m in ternary.Matches(File.ReadAllText(f)))
                    foreach (var g in new[] { m.Groups[1].Value, m.Groups[2].Value })
                        if (!en.ContainsKey(g)) missing.Add($"{Path.GetFileName(f)}: {g}");
            check(missing.Count == 0, "三元表达式里的键不在表里：\n" + string.Join("\n", missing));

            // 2. 所有语言下生成的全部文字不含 |，且 L.Missing 为空
            foreach (var locale in new[] { "zh", "en", "ru", "de", "fr", "es", "pl", "ja" })
            {
                var table = LoadTable(locale);
                L.Use(locale, table, en);

                var d = new RulesData();
                Examples.BatteryRotation(d);
                foreach (var spec in ProfessionExamples.All) ProfessionExamples.Build(d, spec.Id);

                var generated = new List<string>();
                foreach (var s in d.Schedules)
                {
                    generated.Add(s.Name);
                    foreach (var r in s.Rules)
                    {
                        generated.Add(r.Name);
                        generated.Add(Text.Rule(d, s, r));
                        foreach (var act in r.Actions) generated.Add(Text.Action(d, s, act));
                        foreach (var line in Card.Sentence(d, s, r))
                            foreach (var seg in line) generated.Add(seg.Text);
                    }
                    foreach (var x in d.Selectors.Where(x => x.Owner == s.Id))
                    {
                        generated.Add(x.Name);
                        generated.Add(Text.Selector(d, x));
                        generated.Add(Text.SelectorShort(d, x));
                    }
                }

                // 模拟器与报告生成文本
                var w = FakeGame.Install(new SimWorld());
                var box = w.AddRoot("storage", 1, "Box");
                var jug = w.Add(box, "jug_dirty", j => j.Num["volumeMl"] = 2500);
                var interp = new Interpreter(w, d);
                var rep = interp.RunShift(Shift.Morning, new[] { new Apprentice { Name = "Worker", Duties = { new Duty { ScheduleId = d.Schedules[0].Id, Shift = Shift.Morning } } } });
                generated.Add(Text.RunSummary(rep));
                foreach (var g in rep.Groups)
                {
                    foreach (var rr in g.Rules) generated.Add(Text.Outcome(rr));
                }

                var shiftRep = new ShiftReport
                {
                    Shift = Shift.Morning,
                    Day = 1,
                    Workers = { ["Worker"] = new[] { 20, 10 } },
                    Entries = { new ReportEntry { Ws = "Test", Rule = "TestRule", Ok = true, Item = "Item", Cost = 1 } }
                };
                generated.Add(shiftRep.Summary());
                generated.Add(shiftRep.Detail());

                var withBar = generated.Where(s => s != null && s.Contains('|')).Take(5).ToList();
                check(withBar.Count == 0, $"[{locale}] 生成文本中包含 '|' 字符（复数模板未解析）：\n" + string.Join("\n", withBar));
                check(L.Missing.Count == 0, $"[{locale}] 生成文本包含缺失键：\n" + string.Join("\n", L.Missing.Take(5)));
            }

            L.Use("zh", LoadTable("zh"), LoadTable("en"));
        });

        test("多语言·英文模式下全量文本无汉字残留", () =>
        {
            var en = LoadTable("en");
            L.Use("en", en, en);

            var d = new RulesData();
            Examples.BatteryRotation(d);
            foreach (var spec in ProfessionExamples.All) ProfessionExamples.Build(d, spec.Id);

            var texts = new List<(string source, string text)>();

            // 1. 全部示例流程、任务、筛选器与卡片
            foreach (var s in d.Schedules)
            {
                texts.Add(($"Schedule:{s.Id}", s.Name));
                foreach (var r in s.Rules)
                {
                    texts.Add(($"Rule:{r.Id}.Name", r.Name));
                    texts.Add(($"Rule:{r.Id}.Text", Text.Rule(d, s, r)));
                    foreach (var a in r.Actions) texts.Add(($"Rule:{r.Id}.Action", Text.Action(d, s, a)));
                    foreach (var line in Card.Sentence(d, s, r))
                        foreach (var seg in line) texts.Add(($"Card:{r.Id}.Seg", seg.Text));
                    foreach (var err in AutoMod.Domain.Rules.Check.RuleErrors(d, s, r)) texts.Add(($"RuleError:{r.Id}", err));
                    foreach (var warn in AutoMod.Domain.Rules.Check.RuleWarnings(d, r)) texts.Add(($"RuleWarn:{r.Id}", warn));
                }
                foreach (var x in d.Selectors.Where(x => x.Owner == s.Id))
                {
                    texts.Add(($"Selector:{x.Id}.Name", x.Name));
                    texts.Add(($"Selector:{x.Id}.Full", Text.Selector(d, x)));
                    texts.Add(($"Selector:{x.Id}.Short", Text.SelectorShort(d, x)));
                    foreach (var err in AutoMod.Domain.Rules.Check.SelectorErrors(d, x)) texts.Add(($"SelectorError:{x.Id}", err));
                }
                foreach (var obj in d.Objects.Where(o => o.Owner == s.Id))
                {
                    texts.Add(($"Object:{obj.Id}.Name", obj.Name));
                }
            }

            // 2. 目录类别、属性、预设、操作、词汇、失败码、班次、技能
            foreach (var t in Catalog.Traits) texts.Add(($"Catalog.Trait:{t.Id}", t.Name));
            foreach (var p in Catalog.Props)
            {
                texts.Add(($"Catalog.Prop:{p.Id}", p.Name));
                if (Catalog.PropNotName(p.Id) != null) texts.Add(($"Catalog.PropNot:{p.Id}", Catalog.PropNotName(p.Id)));
            }
            foreach (var ps in Catalog.Presets) texts.Add(($"Catalog.Preset:{ps.Id}", ps.Name));
            foreach (var op in Catalog.Ops) texts.Add(($"Catalog.Op:{op.Id}", op.Name));
            foreach (var af in Vocab.AggFns) texts.Add(($"Vocab.Agg:{af.id}", af.name));
            foreach (var sv in Vocab.SysVars) texts.Add(($"Vocab.Sys:{sv.id}", sv.name));

            foreach (var c in Category.All)
                texts.Add(($"Category:{c}", Category.DisplayName(c)));
            foreach (var s in new[] { Shift.Morning, Shift.Night })
                texts.Add(($"Shift:{s}", Shift.DisplayName(s)));
            foreach (var fc in new[] { FailCode.NoSource, FailCode.NoTool, FailCode.DestFull, FailCode.NotAllowed, FailCode.NoEffect, FailCode.NeedsPlayer, FailCode.NoAp, FailCode.Many, FailCode.NoMembers, FailCode.Error, FailCode.Simulated })
                texts.Add(($"FailCode:{fc}", FailCode.DisplayName(fc)));

            // 3. 策略模拟器运行与报告生成（FakeGame 在 en 下提供英文动作结果）
            var w = FakeGame.Install(new SimWorld());
            var box = w.AddRoot("storage", 1, "Storage Bay");
            var pitcher = w.AddRoot("water_pitcher", 2, "Water Pitcher");
            w.Add(pitcher, "filter", f => f.Num["durability"] = 20);
            var tools = w.AddRoot("toolbox", 3, "Toolbox");
            w.Add(tools, "labeler");
            var sch = d.Schedules[0];
            var a1 = new Apprentice { Name = "Worker", Duties = { new Duty { ScheduleId = sch.Id, Shift = Shift.Morning } } };
            var interp = new Interpreter(w, d);
            var runReport = interp.RunShift(Shift.Morning, new[] { a1 });
            texts.Add(("RunReport.Summary", Text.RunSummary(runReport)));
            foreach (var g in runReport.Groups)
            {
                foreach (var rr in g.Rules) texts.Add(("Rule.Outcome", Text.Outcome(rr)));
            }
            var shiftReport = new ShiftReport
            {
                Shift = Shift.Morning,
                Day = 1,
                Workers = { ["Worker"] = new[] { 20, 10 } },
                Entries = { new ReportEntry { Ws = "Test", Rule = "TestRule", Ok = true, Item = "Item", Cost = 1 } }
            };
            texts.Add(("ShiftReport.Summary", shiftReport.Summary()));
            texts.Add(("ShiftReport.Detail", shiftReport.Detail()));

            // 4. 手册全部条目
            foreach (var kv in en.Where(k => k.Key.StartsWith("handbook.")))
                texts.Add(($"Handbook:{kv.Key}", L.T(kv.Key)));

            // 检查全部文本无汉字
            var hanziViolations = new List<string>();
            foreach (var (src, str) in texts)
            {
                if (string.IsNullOrEmpty(str)) continue;
                if (str.Any(c => c >= 0x4E00 && c <= 0x9FFF))
                    hanziViolations.Add($"[{src}]: {str}");
            }

            check(hanziViolations.Count == 0, $"英文模式下发现 {hanziViolations.Count} 处包含汉字：\n" + string.Join("\n", hanziViolations.Take(10)));
            check(L.Missing.Count == 0, $"英文模式下有 {L.Missing.Count} 个缺失键：\n" + string.Join("\n", L.Missing.Take(10)));

            L.Use("zh", LoadTable("zh"), LoadTable("en"));
        });

        test("多语言·手册各页文字量与不超出纸面检查", () =>
        {
            var enTable = LoadTable("en");
            var zhTable = LoadTable("zh");

            // 7 页手册的结构定义（与 HandbookPages.All 保持一致）
            var pageSpecs = new (string kind, string[] keys)[][]
            {
                // p1 快速上手、学徒（AP、多个流程）
                new[]
                {
                    ("banner", new[] { "handbook.p1.banner_quickstart" }),
                    ("steps", new[] { "handbook.p1.steps_quickstart" }),
                    ("tip", new[] { "handbook.p1.tip_payroll" }),
                    ("banner", new[] { "handbook.p1.banner_apprentices" }),
                    ("entry", new[] { "handbook.p1.title_ap", "handbook.p1.text_ap" }),
                    ("entry", new[] { "handbook.p1.title_multi_sched", "handbook.p1.text_multi_sched" }),
                },
                // p2 熟练度与等级
                new[]
                {
                    ("banner", new[] { "handbook.p2.banner" }),
                    ("entry", new[] { "handbook.p2.title_prof", "handbook.p2.text_prof" }),
                    ("entry", new[] { "handbook.p2.title_switch", "handbook.p2.text_switch" }),
                    ("entry", new[] { "handbook.p2.title_level", "handbook.p2.text_level" }),
                    ("tip", new[] { "handbook.p2.tip" }),
                },
                // p3 流程的组成
                new[]
                {
                    ("banner", new[] { "handbook.p3.banner" }),
                    ("entry", new[] { "handbook.p3.title_scope", "handbook.p3.text_scope" }),
                    ("entry", new[] { "handbook.p3.title_filter", "handbook.p3.text_filter" }),
                    ("tip", new[] { "handbook.p3.tip_name" }),
                    ("entry", new[] { "handbook.p3.title_range", "handbook.p3.text_range" }),
                    ("steps", new[] { "handbook.p3.steps_pick" }),
                    ("entry", new[] { "handbook.p3.title_param", "handbook.p3.text_param" }),
                    ("steps", new[] { "handbook.p3.steps_conds" }),
                },
                // p4 任务与动作
                new[]
                {
                    ("banner", new[] { "handbook.p4.banner" }),
                    ("entry", new[] { "handbook.p4.title_pre", "handbook.p4.text_pre" }),
                    ("entry", new[] { "handbook.p4.title_move", "handbook.p4.text_move" }),
                    ("entry", new[] { "handbook.p4.title_swap", "handbook.p4.text_swap" }),
                    ("entry", new[] { "handbook.p4.title_use", "handbook.p4.text_use" }),
                    ("entry", new[] { "handbook.p4.title_press", "handbook.p4.text_press" }),
                    ("tip", new[] { "handbook.p4.tip" }),
                    ("tip", new[] { "handbook.p4.tip_sub" }),
                },
                // p5 自动换电与执行顺序
                new[]
                {
                    ("banner", new[] { "handbook.p5.banner_swap" }),
                    ("diagram", Array.Empty<string>()),
                    ("steps", new[] { "handbook.p5.steps_swap" }),
                    ("banner", new[] { "handbook.p5.banner_order" }),
                    ("entry", new[] { "handbook.p5.title_seq", "handbook.p5.text_seq" }),
                },
                // p6 示例流程
                new[]
                {
                    ("banner", new[] { "handbook.p6.banner" }),
                    ("entry", new[] { "handbook.p6.title_battery", "handbook.p6.text_battery" }),
                    ("entry", new[] { "handbook.p6.title_organize", "handbook.p6.text_organize" }),
                    ("entry", new[] { "handbook.p6.title_hydro", "handbook.p6.text_hydro" }),
                    ("entry", new[] { "handbook.p6.title_ras", "handbook.p6.text_ras" }),
                    ("entry", new[] { "handbook.p6.title_rat", "handbook.p6.text_rat" }),
                    ("entry", new[] { "handbook.p6.title_print", "handbook.p6.text_print" }),
                    ("entry", new[] { "handbook.p6.title_vending", "handbook.p6.text_vending" }),
                    ("tip", new[] { "handbook.p6.tip" }),
                },
                // p7 策略模拟器与报告
                new[]
                {
                    ("banner", new[] { "handbook.p7.banner" }),
                    ("entry", new[] { "handbook.p7.title_sim", "handbook.p7.text_sim" }),
                    ("entry", new[] { "handbook.p7.title_report", "handbook.p7.text_report" }),
                    ("tip", new[] { "handbook.p7.tip" }),
                },
            };

            var testLocales = new[] { "zh", "en", "ru", "de", "fr", "es", "pl", "ja" };
            foreach (var loc in testLocales)
            {
                var curTable = LoadTable(loc);
                L.Use(loc, curTable, enTable);

                bool isZh = loc == "zh";
                bool isCjk = loc == "zh" || loc == "ja";
                float gap = curTable.TryGetValue("layout.handbook.gap", out string gs) && float.TryParse(gs, out float fg) ? fg : (isZh ? 8f : 6f);
                float bodySize = curTable.TryGetValue("layout.handbook.body_size", out string bs) && float.TryParse(bs, out float fbs) ? fbs : (isZh ? 15f : 13f);
                const float contentW = 508f, top = 74f, contentBottom = 700f, textLeft = 68f;

                int EstLines(string text, float width, float fontSize)
                {
                    int totalLines = 0;
                    foreach (var para in text.Split('\n'))
                    {
                        if (string.IsNullOrEmpty(para)) { totalLines++; continue; }
                        if (isCjk)
                        {
                            float curW = 0;
                            int lines = 1;
                            foreach (char c in para)
                            {
                                float cw = c > 0x2E7F ? fontSize : fontSize * 0.56f;
                                if (curW + cw > width) { lines++; curW = cw; }
                                else curW += cw;
                            }
                            totalLines += lines;
                        }
                        else
                        {
                            var words = para.Split(' ');
                            float curW = 0;
                            int lines = 1;
                            float spaceW = fontSize * 0.3f;
                            foreach (var w in words)
                            {
                                float ww = w.Sum(c => c > 0x2E7F ? fontSize : fontSize * 0.56f);
                                if (curW == 0) curW = ww;
                                else if (curW + spaceW + ww <= width) curW += spaceW + ww;
                                else { lines++; curW = ww; }
                            }
                            totalLines += lines;
                        }
                    }
                    return totalLines;
                }

                float TextHeight(string text, float width, float fontSize)
                {
                    int lines = EstLines(text, width, fontSize);
                    return lines * fontSize * 1.3f + 2f;
                }

                for (int pageIdx = 0; pageIdx < pageSpecs.Length; pageIdx++)
                {
                    int pageNum = pageIdx + 1;
                    float y = top;
                    var blocks = pageSpecs[pageIdx];

                    foreach (var (kind, keys) in blocks)
                    {
                        float h = 0;
                        if (kind == "banner")
                        {
                            h = 26f;
                            check(!string.IsNullOrEmpty(L.T(keys[0])), $"[{loc}] p{pageNum} 横幅 {keys[0]} 不能为空");
                        }
                        else if (kind == "steps")
                        {
                            string text = L.T(keys[0]);
                            check(!string.IsNullOrEmpty(text), $"[{loc}] p{pageNum} 步骤 {keys[0]} 不能为空");
                            h = TextHeight(text, contentW, bodySize);
                        }
                        else if (kind == "tip")
                        {
                            string text = L.T(keys[0]);
                            check(!string.IsNullOrEmpty(text), $"[{loc}] p{pageNum} 提示 {keys[0]} 不能为空");
                            float tw = contentW - 5f - 28f;
                            h = TextHeight(text, tw, bodySize - 1) + 14f;
                        }
                        else if (kind == "entry")
                        {
                            string title = L.T(keys[0]);
                            string text = L.T(keys[1]);
                            check(!string.IsNullOrEmpty(title), $"[{loc}] p{pageNum} 条目标题 {keys[0]} 不能为空");
                            check(!string.IsNullOrEmpty(text), $"[{loc}] p{pageNum} 条目正文 {keys[1]} 不能为空");
                            float w = contentW - textLeft;
                            float th = TextHeight(title, w, bodySize);
                            float bh = TextHeight(text, w, bodySize - 1);
                            h = Math.Max(56f, th + 2f + bh);
                        }
                        else if (kind == "diagram")
                        {
                            h = 300f;
                            string[] diagKeys = {
                                "handbook.diagram.recharger", "handbook.diagram.recharger_note",
                                "handbook.diagram.machines", "handbook.diagram.machines_note",
                                "handbook.diagram.storage", "handbook.diagram.storage_note",
                                "handbook.diagram.arrow_full", "handbook.diagram.arrow_empty",
                                "handbook.diagram.arrow_charge"
                            };
                            foreach (var dk in diagKeys)
                                check(!string.IsNullOrEmpty(L.T(dk)), $"[{loc}] 示意图文本 {dk} 不能为空");
                        }

                        y += h + gap * 1.5f;
                    }

                    check(y <= contentBottom, $"[{loc}] 第 {pageNum} 页内容超出安全上限 {contentBottom}（估算 y={y:0.1} > {contentBottom}）");
                }
            }

            L.Use("zh", zhTable, enTable);
        });

        test("多语言·手册键内容与数字方括号引号防错位检查", () =>
        {
            var enTable = LoadTable("en");
            var zhTable = LoadTable("zh");
            var targetLocales = new[] { "de", "ru", "ja", "fr", "es", "pl" };
            var numRegex = new Regex(@"\d+(?:\.\d+)?");
            var brkRegex = new Regex(@"\[.*?\]|【.*?】");
            var quoteRegex = new Regex("\"[^\"]+\"|«[^»]+»|「[^」]+」");

            var hbKeys = enTable.Keys.Where(k => k.StartsWith("handbook.")).ToList();

            foreach (var loc in targetLocales)
            {
                var curTable = LoadTable(loc);
                var failures = new List<string>();

                foreach (var k in hbKeys)
                {
                    string enVal = enTable[k];
                    if (!curTable.TryGetValue(k, out string curVal))
                    {
                        failures.Add($"[{loc}] 缺少键 {k}");
                        continue;
                    }

                    // 1. 数字集合比对
                    var enNums = numRegex.Matches(enVal).Select(m => m.Value).ToHashSet();
                    var curNums = numRegex.Matches(curVal).Select(m => m.Value).ToHashSet();
                    if (!enNums.SetEquals(curNums))
                    {
                        failures.Add($"[{loc}] 键 {k} 数字不匹配: en=[{string.Join(",", enNums)}] vs loc=[{string.Join(",", curNums)}]");
                    }

                    // 2. 方括号示例名个数比对
                    int enBrkCount = brkRegex.Matches(enVal).Count;
                    int curBrkCount = brkRegex.Matches(curVal).Count;
                    if (enBrkCount != curBrkCount)
                    {
                        failures.Add($"[{loc}] 键 {k} 方括号数量不匹配: en={enBrkCount} vs loc={curBrkCount}");
                    }

                    // 3. 带引号按钮名个数比对
                    int enQuoteCount = quoteRegex.Matches(enVal).Count;
                    int curQuoteCount = quoteRegex.Matches(curVal).Count;
                    if (enQuoteCount != curQuoteCount)
                    {
                        failures.Add($"[{loc}] 键 {k} 引号数量不匹配: en={enQuoteCount} vs loc={curQuoteCount}");
                    }
                }

                // 4. 同一语言内各页标题 (handbook.pN.banner*) 互不相同
                var bannerRegex = new Regex(@"^handbook\.p\d+\.banner");
                var banners = curTable.Where(kv => bannerRegex.IsMatch(kv.Key)).Select(kv => (kv.Key, kv.Value)).ToList();
                var dupBanners = banners.GroupBy(b => b.Value).Where(g => g.Count() > 1).ToList();
                foreach (var dup in dupBanners)
                {
                    failures.Add($"[{loc}] 存在重复页面横幅标题 \"{dup.Key}\": 键包括 [{string.Join(", ", dup.Select(x => x.Key))}]");
                }

                check(failures.Count == 0, $"[{loc}] 手册键内容检查发现 {failures.Count} 处错误：\n" + string.Join("\n", failures));
            }
        });

        test("多语言·手册第5页示意图标注与框宽无折行重叠检查", () =>
        {
            var enTable = LoadTable("en");
            var zhTable = LoadTable("zh");
            var testLocales = new[] { "zh", "en", "ru", "de", "fr", "es", "pl", "ja" };

            int EstLines(string text, float width, float fontSize, bool isCjk)
            {
                int totalLines = 0;
                foreach (var para in text.Split('\n'))
                {
                    if (string.IsNullOrEmpty(para)) { totalLines++; continue; }
                    if (isCjk)
                    {
                        float curW = 0;
                        int lines = 1;
                        foreach (char c in para)
                        {
                            float cw = c > 0x2E7F ? fontSize : fontSize * 0.56f;
                            if (curW + cw > width) { lines++; curW = cw; }
                            else curW += cw;
                        }
                        totalLines += lines;
                    }
                    else
                    {
                        var words = para.Split(' ');
                        float curW = 0;
                        int lines = 1;
                        float spaceW = fontSize * 0.3f;
                        foreach (var w in words)
                        {
                            float ww = w.Sum(c => c > 0x2E7F ? fontSize : fontSize * 0.56f);
                            if (curW == 0) curW = ww;
                            else if (curW + spaceW + ww <= width) curW += spaceW + ww;
                            else { lines++; curW = ww; }
                        }
                        totalLines += lines;
                    }
                }
                return totalLines;
            }

            foreach (var loc in testLocales)
            {
                var curTable = LoadTable(loc);
                bool isZh = loc == "zh";
                bool isCjk = loc == "zh" || loc == "ja";
                var failures = new List<string>();

                // 1. 箭头标注宽度与行数
                string af = curTable["handbook.diagram.arrow_full"];
                string ae = curTable["handbook.diagram.arrow_empty"];
                string ac = curTable["handbook.diagram.arrow_charge"];

                float afW = curTable.TryGetValue("layout.handbook.diagram.arrow_full_w", out var sw1) && float.TryParse(sw1, out var w1) ? w1 : 160f;
                float aeW = curTable.TryGetValue("layout.handbook.diagram.arrow_empty_w", out var sw2) && float.TryParse(sw2, out var w2) ? w2 : 160f;
                float acW = curTable.TryGetValue("layout.handbook.diagram.arrow_charge_w", out var sw3) && float.TryParse(sw3, out var w3) ? w3 : 230f;

                const float maxArrowLen = 166f;
                if (afW > maxArrowLen) failures.Add($"[{loc}] arrow_full_w ({afW}) 超过对应箭头长度 {maxArrowLen}");
                if (aeW > maxArrowLen) failures.Add($"[{loc}] arrow_empty_w ({aeW}) 超过对应箭头长度 {maxArrowLen}");

                int afLines = EstLines(af, afW, 13f, isCjk);
                if (afLines != 1) failures.Add($"[{loc}] arrow_full 发生折行 ({afLines} 行): \"{af}\"");

                int aeLines = EstLines(ae, aeW, 13f, isCjk);
                if (aeLines != 1) failures.Add($"[{loc}] arrow_empty 发生折行 ({aeLines} 行): \"{ae}\"");

                int acLines = EstLines(ac, acW, 13f, isCjk);
                if (acLines != 1) failures.Add($"[{loc}] arrow_charge 发生折行 ({acLines} 行): \"{ac}\"");

                // 2. 上下箭头标注无重叠
                float lyFull = curTable.TryGetValue("layout.handbook.diagram.arrow_full_y", out var s1) && float.TryParse(s1, out var v1) ? v1 :
                               (curTable.TryGetValue("layout.handbook.p5_full_y", out var sf) && float.TryParse(sf, out var vf) ? vf : (isZh ? 8f : 6f));
                float lyEmpty = curTable.TryGetValue("layout.handbook.diagram.arrow_empty_y", out var s2) && float.TryParse(s2, out var v2) ? v2 :
                                (curTable.TryGetValue("layout.handbook.p5_empty_y", out var se) && float.TryParse(se, out var ve) ? ve : (isZh ? 58f : 56f));
                if (lyFull + 20f > lyEmpty)
                {
                    failures.Add($"[{loc}] 上下标注垂直间距不足重叠: lyFull={lyFull}, lyEmpty={lyEmpty}");
                }

                // 3. 框内文字单行不折行
                float rw = curTable.TryGetValue("layout.handbook.diagram.recharger_w", out var srw) && float.TryParse(srw, out var vrw) ? vrw : 150f;
                float mw = curTable.TryGetValue("layout.handbook.diagram.machines_w", out var smw) && float.TryParse(smw, out var vmw) ? vmw : 150f;
                float sw = curTable.TryGetValue("layout.handbook.diagram.storage_w", out var ssw) && float.TryParse(ssw, out var vsw) ? vsw : 150f;

                var nodes = new (string key, float boxW, float fs)[]
                {
                    ("handbook.diagram.recharger", rw, 15f),
                    ("handbook.diagram.recharger_note", rw, 11f),
                    ("handbook.diagram.machines", mw, 15f),
                    ("handbook.diagram.machines_note", mw, 11f),
                    ("handbook.diagram.storage", sw, 15f),
                    ("handbook.diagram.storage_note", sw, 11f),
                };

                foreach (var (k, boxW, fs) in nodes)
                {
                    string text = curTable[k];
                    int lines = EstLines(text, boxW, fs, isCjk);
                    if (lines != 1) failures.Add($"[{loc}] 示意图框节点 {k} 发生折行 ({lines} 行): \"{text}\"");
                }

                check(failures.Count == 0, $"[{loc}] 第 5 页示意图检查失败：\n" + string.Join("\n", failures));
            }
        });

        test("多语言·手册非中文避免弯引号检查", () =>
        {
            var testLocales = new[] { "en", "de", "ru", "ja", "fr", "es", "pl" };
            char[] curvedQuotes = { '“', '”', '„' };

            foreach (var loc in testLocales)
            {
                var curTable = LoadTable(loc);
                var violations = new List<string>();

                foreach (var (k, v) in curTable)
                {
                    if (!k.StartsWith("handbook.")) continue;
                    if (v.IndexOfAny(curvedQuotes) >= 0)
                    {
                        violations.Add($"[{loc}] 键 {k} 包含弯引号: \"{v}\"");
                    }
                }

                check(violations.Count == 0, $"[{loc}] 手册文本中禁止使用弯引号 “ ” „：\n" + string.Join("\n", violations));
            }
        });

        test("多语言·动态键覆盖与各语言解析有效性", () =>
        {
            var en = LoadTable("en");
            string srcRoot = Path.Combine(FindRoot(), "src");

            // 1. 明确收集各动态键前缀及其全部已知合法取值
            var dynamicPatterns = new Dictionary<string, List<string>>
            {
                ["item_group."] = ItemGroups.Order.ToList(),
                ["failcode."] = new()
                {
                    "no_source", "no_tool", "dest_full", "destfull", "many", "not_allowed", "notallowed",
                    "no_effect", "noeffect", "simulated", "needs_player", "needsplayer", "no_ap", "noap",
                    "no_members", "nomembers", "error", "other"
                },
                ["card.param."] = new() { "button", "list", "pre", "to", "tool" },
                ["trait."] = Catalog.Traits.Select(t => t.Id.ToLowerInvariant()).ToList(),
                ["prop."] = Catalog.Props.Select(p => p.Id.ToLowerInvariant())
                    .Concat(Catalog.Props.Where(p => Catalog.IsBool(p.Id)).Select(p => p.Id.ToLowerInvariant() + ".not"))
                    .Concat(new[] { "purity.grade_short" })
                    .ToList(),
                ["preset."] = Catalog.Presets.Select(p => p.Id.ToLowerInvariant()).ToList(),
                ["op."] = Catalog.Ops.Select(o => o.Id.ToLowerInvariant()).ToList(),
                ["kind."] = new()
                {
                    "animal_cage", "any", "any_family", "any_type", "bottle_printer", "energy_credit",
                    "feces_large", "feces_medium", "feces_small", "fillerweed", "fillerweed_seed",
                    "fish_scrap", "hydroponic", "industrial_fish_feed", "kitchen_cleaver", "labeler",
                    "large_bottled_water", "livestock_feed_dispenser", "morsel", "nutrient_tablet",
                    "nutrifruit", "nutrifruit_seed", "printer", "printer_chip_pistol", "printer_plastic",
                    "ras", "ras_water_tank", "rat", "raw_meat", "recharger", "self", "small_bottled_water",
                    "small_morsel", "small_raw_meat", "tilapia", "vending_fountain", "water_filter",
                    "water_filter_adv", "water_jug", "water_purifier", "wire_cutter"
                },
                ["slot."] = new()
                {
                    "backpack", "base_water", "batteries", "battery", "butcher", "chip", "clean_water",
                    "container", "core", "dispenser", "distillate", "filter", "filters", "frame",
                    "input", "module", "note", "nutrient", "output", "power", "recipe", "seed",
                    "storage", "tank", "trap", "water", "wine"
                },
                ["sim.item."] = new()
                {
                    "animal_cage", "battery_empty", "battery_full", "battery_self", "filter", "filter_adv",
                    "jug_dirty", "jug_empty", "knife", "labeler", "machine", "meat", "moisture_farm",
                    "other", "projector", "rat_baby", "rat_f", "rat_fat", "rat_m", "recharger", "storage",
                    "toolbox", "water_pitcher", "water_purifier", "wire_cutter"
                },
                ["skill."] = new() { "husbandry", "maintenance", "misc", "power", "water" },
                ["shift."] = new() { "morning", "night", "off" },
                ["shift.short."] = new() { "morning", "night", "off" },
                ["btn."] = new()
                {
                    "mech_vending_btn_transfer", "mech_bottle_printer_btn_print_small", "mech_bottle_printer_btn_print_large"
                }
            };

            // 2. 检查源码里所有 L.T("xxx." + ...) 是否都已在 dynamicPatterns 中声明并被覆盖
            var dynRegex = new Regex(@"L\.T\(\s*""([a-z0-9_]+\.)""\s*\+");
            var foundPrefixes = new HashSet<string>();
            foreach (var f in Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(f);
                foreach (Match m in dynRegex.Matches(text))
                {
                    string prefix = m.Groups[1].Value;
                    foundPrefixes.Add(prefix);
                }
            }

            foreach (var p in foundPrefixes)
            {
                if (p == "names.kind.") continue; // 适配层名称查不到时退回游戏名，不走 L.T 纯前缀
                check(dynamicPatterns.ContainsKey(p), $"发现未登记的动态键前缀：[{p}]");
            }

            // 3. 断言每个动态键值在 en.csv 都有
            var allDynamicKeys = new List<string>();
            foreach (var (prefix, values) in dynamicPatterns)
            {
                foreach (var val in values)
                {
                    string fullKey = prefix + val;
                    allDynamicKeys.Add(fullKey);
                    check(en.ContainsKey(fullKey), $"en.csv 缺少动态键：[{fullKey}]");
                }
            }

            // 4. 所有 8 种语言下，L.T 返回值不能等于键本身
            foreach (var locale in new[] { "zh", "en", "ru", "de", "fr", "es", "pl", "ja" })
            {
                var table = LoadTable(locale);
                L.Use(locale, table, en);

                foreach (var key in allDynamicKeys)
                {
                    string res = L.T(key);
                    check(!string.IsNullOrEmpty(res) && res != key, $"[{locale}] 动态键 [{key}] 解析失败，返回了键本身或空值");
                }
            }

            L.Use("zh", LoadTable("zh"), LoadTable("en"));
        });

        test("多语言·0.8 中文界面字面量在 zh.csv 中完全覆盖", () =>
        {
            string txtPath = Path.Combine(FindRoot(), "tests", "AutoMod.Domain.Tests", "zh_ui_08.txt");
            check(File.Exists(txtPath), "zh_ui_08.txt 文件存在");
            var zhTable = LoadTable("zh");
            var holeRegex = new Regex(@"\{[^}]*\}");
            var zhBlob = string.Join("\n", zhTable.Values.Select(v => holeRegex.Replace(v, "\0")));

            var lines = File.ReadAllLines(txtPath).Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l));
            int checkedCount = 0;
            foreach (var line in lines)
            {
                string target = line.Replace("\\n", "\n").Trim();
                check(zhBlob.Contains(target), $"0.8 中文文本 [{line}] 未在 zh.csv 的值中找到");
                checkedCount++;
            }
            check(checkedCount > 500, $"检查了足够数量的 0.8 中文字面量 (实际 {checkedCount})");
        });

        test("多语言·0.8 中文位置映射在 zh.csv 中逐条匹配", () =>
        {
            string tsvPath = Path.Combine(FindRoot(), "tests", "AutoMod.Domain.Tests", "zh_ui_08_map.tsv");
            check(File.Exists(tsvPath), "zh_ui_08_map.tsv 文件存在");
            var zhTable = LoadTable("zh");
            var holeRegex = new Regex(@"\{[^}]*\}");
            var tagRegex = new Regex(@"<[^>]+>");

            string Shape(string s)
            {
                s = holeRegex.Replace(s, "○");
                s = tagRegex.Replace(s, "");
                return s.Replace("\\n", "\n").Trim();
            }

            var lines = File.ReadAllLines(tsvPath);
            int checkedCount = 0;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('\t');
                check(parts.Length == 2, $"TSV 格式错误：[{line}]");
                string key = parts[0];
                string expected = parts[1];

                check(zhTable.ContainsKey(key), $"zh.csv 缺少位置匹配键：[{key}]");
                string actual = zhTable[key];
                check(Shape(actual) == Shape(expected),
                    $"键 [{key}] 的 zh 值 [{actual}] 与 0.8 原文 [{expected}] 不一致 (shaped actual: [{Shape(actual)}], expected: [{Shape(expected)}])");
                checkedCount++;
            }
            check(checkedCount > 500, $"检查了足够数量的位置对照项 (实际 {checkedCount})");
        });

        test("多语言·值首尾有空白必须加双引号", () =>
        {
            string locDir = Path.Combine(FindRoot(), "src", "AutoMod", "ModFiles", "Localization");
            var languages = new[] { "en", "zh", "ru", "de", "fr", "es", "pl", "ja" };
            foreach (var lang in languages)
            {
                string path = Path.Combine(locDir, $"{lang}.csv");
                check(File.Exists(path), $"{lang}.csv 存在");
                var lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string rawLine = lines[i];
                    if (string.IsNullOrWhiteSpace(rawLine) || rawLine.TrimStart().StartsWith("#")) continue;
                    int comma = rawLine.IndexOf(',');
                    if (comma < 0) continue;
                    string rawVal = rawLine.Substring(comma + 1);
                    bool isQuoted = rawVal.Length >= 2 && rawVal.StartsWith("\"") && rawVal.EndsWith("\"");
                    if (!isQuoted)
                    {
                        check(rawVal == rawVal.Trim(),
                            $"{lang}.csv 第 {i + 1} 行值存在首尾空格但未加引号：[{rawLine}]");
                    }
                }
            }
        });

        test("多语言·英文复数键首个形式不能为复数", () =>
        {
            string enPath = Path.Combine(FindRoot(), "src", "AutoMod", "ModFiles", "Localization", "en.csv");
            check(File.Exists(enPath), "en.csv 存在");
            var enTable = LoadTable("en");
            var pluralWords = new Regex(@"\b(items|tasks|shifts|times|places|units)\b", RegexOptions.IgnoreCase);

            int checkedPluralKeys = 0;
            foreach (var (k, v) in enTable)
            {
                if (!v.Contains('|')) continue;
                var forms = v.Split('|');
                check(forms.Length >= 2, $"en 键 [{k}] 的复数格式至少应有两项");
                string form0 = forms[0].Trim();
                check(!pluralWords.IsMatch(form0),
                    $"en 键 [{k}] 的第一个形式包含复数词：[{form0}] (完整值: [{v}])");
                checkedPluralKeys++;
            }
            check(checkedPluralKeys > 10, $"检查了足够数量的英文复数键 (实际 {checkedPluralKeys})");
        });

        test("多语言·表里值为空串的键返回空串，不算缺失", () =>
        {
            var cur = new Dictionary<string, string> { ["editor.times_unit"] = "" };
            var en = new Dictionary<string, string> { ["editor.times_unit"] = "x", ["a"] = "A" };
            L.Use("en", cur, en);
            check(L.T("editor.times_unit") == "", "空串值返回空串");
            check(!L.Missing.Contains("editor.times_unit"), "空串值不记入 Missing");
            check(L.T("a") == "A", "缺的键仍退到英文");
        });

        test("多语言·各语言禁用别译检查", () =>
        {
            var rules = new (string locale, Regex regex, string desc)[]
            {
                ("en", new Regex(@"\b(workflow|selector|policy|schedule|rule|duty)s?\b", RegexOptions.IgnoreCase), "workflow/selector/policy/schedule/rule/duty"),
                ("de", new Regex(@"\b(Ablauf|Abläufe|Abläufen|Selektor|Selektoren)\b", RegexOptions.IgnoreCase), "Ablauf/Abläufe/Selektor"),
                ("ja", new Regex(@"ワークフロー|セレクター|セレクタ|休みに設定", RegexOptions.None), "ワークフロー/セレクター/休みに設定"),
                ("es", new Regex(@"\b(selector|selectores|flujo|flujos)\b", RegexOptions.IgnoreCase), "selector/flujo"),
                ("fr", new Regex(@"\b(sélecteur|sélecteurs|flux)\b", RegexOptions.IgnoreCase), "sélecteur/flux"),
                ("pl", new Regex(@"\b(selektor|selektory|selektorów|czeladnik[a-z]*|proces(y|ów|em|ie|owi|ach|ami|om)?|uczeń|ucznia|uczniowi|uczniem|uczniu|uczniowie|uczniów|uczniom|uczniami|uczniach)\b", RegexOptions.IgnoreCase), "selektor/czeladnik/proces/uczeń"),
                ("ru", new Regex(@"\b(распоряд(ок|ка|ку|ком|ке|ки|ков|кам|ками|ках|ковах))\b", RegexOptions.IgnoreCase), "распорядок"),
            };

            foreach (var (loc, regex, desc) in rules)
            {
                var table = LoadTable(loc);
                foreach (var (k, v) in table)
                {
                    check(!regex.IsMatch(v), $"{loc} 键 [{k}] 含禁用术语 {desc}：[{v}]");
                }
            }
        });

        test("多语言·按语言估算按钮与标签宽度不超出预留宽度", () =>
        {
            static float EstimateWidth(string text, int size = 13)
            {
                float w = 0;
                foreach (char c in text) w += c > 0x2E7F ? size : size * 0.56f;
                return w;
            }

            foreach (string loc in new[] { "zh", "en", "de", "fr", "es", "pl", "ru", "ja" })
            {
                var table = LoadTable(loc);

                // Table.More 详情/折叠按钮宽度与对讲机报告页详情按钮宽度
                float btnMoreW = table.TryGetValue("layout.btn_more_width", out string bmw) && float.TryParse(bmw, out float fbmw) ? fbmw : 95f;
                string collapse = table.GetValueOrDefault("ui.collapse", "");
                string details = table.GetValueOrDefault("table.btn_details", "");
                string intercomDetails = table.GetValueOrDefault("intercom.btn_details", "");
                check(EstimateWidth(collapse) + 16 <= btnMoreW, $"{loc} ui.collapse [{collapse}] 宽度超出 btn_more_width {btnMoreW}");
                check(EstimateWidth(details) + 16 <= btnMoreW, $"{loc} table.btn_details [{details}] 宽度超出 btn_more_width {btnMoreW}");
                check(intercomDetails == details, $"{loc} intercom.btn_details [{intercomDetails}] 应与 table.btn_details [{details}] 一致");
                check(EstimateWidth(intercomDetails) + 16 <= btnMoreW, $"{loc} intercom.btn_details [{intercomDetails}] 宽度超出 btn_more_width {btnMoreW}");

                // ui.name 标签宽度 (预留 65px)
                string name = table.GetValueOrDefault("ui.name", "");
                check(EstimateWidth(name) <= 65, $"{loc} ui.name [{name}] 宽度超出 65");

                // schedule.cond_always 下拉宽度
                float whenW = table.TryGetValue("layout.editor.when_dropdown_width", out string wdw) && float.TryParse(wdw, out float fwdw) ? fwdw : 180f;
                string always = table.GetValueOrDefault("schedule.cond_always", "");
                check(EstimateWidth(always) + 24 <= whenW, $"{loc} schedule.cond_always [{always}] 宽度超出 when_dropdown_width {whenW}");

                // 新建流程窗口按钮宽度与示例名文字（文字宽度 <= 按钮宽度）
                float newSchBtnW = table.TryGetValue("layout.new_schedule.btn_width", out string nsbw) && float.TryParse(nsbw, out float fnsbw) ? fnsbw : 170f;
                string[] newSchKeys = {
                    "schedule.new_blank", "example.battery.name",
                    "example.spec.tidy.name", "example.spec.hydro.name",
                    "example.spec.aqua.name", "example.spec.ranch.name",
                    "example.spec.gun.name", "example.spec.water.name"
                };
                foreach (var nsk in newSchKeys)
                {
                    string btnText = table.GetValueOrDefault(nsk, "");
                    check(!string.IsNullOrEmpty(btnText), $"[{loc}] {nsk} 不能为空");
                    float tw = EstimateWidth(btnText, 13);
                    check(tw <= newSchBtnW, $"[{loc}] 新建流程按钮 [{btnText}] 字宽 {tw:0.1} > 按钮宽度 {newSchBtnW}");
                    FitText(btnText, newSchBtnW, 13, out bool cutBtn);
                    check(!cutBtn, $"[{loc}] 新建流程按钮 [{btnText}] 在宽度 {newSchBtnW} 下被截断");
                }
            }
        });

        test("多语言·全部示例规则整句与卡片无缺失键与英文and/or残留", () =>
        {
            var enTable = LoadTable("en");
            var andOrRegex = new Regex(@"\b(and|or)\b", RegexOptions.IgnoreCase);

            foreach (string loc in new[] { "zh", "en", "de", "fr", "es", "pl", "ru", "ja" })
            {
                var curTable = LoadTable(loc);
                L.Use(loc, curTable, enTable);

                var d = new RulesData();
                var schedules = new List<Schedule>();
                foreach (var spec in ProfessionExamples.All) schedules.Add(ProfessionExamples.Build(d, spec.Id));
                schedules.Add(Examples.BatteryRotation(d));

                foreach (var s in schedules)
                {
                    foreach (var r in s.Rules)
                    {
                        string ruleText = Text.Rule(d, s, r);
                        check(L.Missing.Count == 0, $"{loc} 规则 [{r.Name}] 生成整句时存在缺失键: {string.Join(", ", L.Missing)}");

                        var card = Card.Sentence(d, s, r);
                        check(L.Missing.Count == 0, $"{loc} 规则 [{r.Name}] 生成卡片时存在缺失键: {string.Join(", ", L.Missing)}");

                        if (loc != "en")
                        {
                            check(!andOrRegex.IsMatch(ruleText), $"{loc} 规则 [{r.Name}] 整句含英文 and/or 残留: [{ruleText}]");
                            foreach (var line in card)
                            {
                                foreach (var seg in line)
                                {
                                    if (!string.IsNullOrEmpty(seg.Text))
                                        check(!andOrRegex.IsMatch(seg.Text), $"{loc} 规则 [{r.Name}] 卡片片段含英文 and/or 残留: [{seg.Text}]");
                                }
                            }
                        }
                    }
                }
            }

            L.Use("zh", LoadTable("zh"), enTable);
        });

        test("多语言·学徒页一行宽度不超出面板宽度", () =>
        {
            static string Fit(string text, float width, int size, out bool cut)
            {
                float W(char c) => c > 0x2E7F ? size : size * 0.56f;
                float total = text.Sum(W);
                cut = total > width;
                if (!cut) return text;
                float room = width - size, used = 0;
                int n = 0;
                while (n < text.Length && used + W(text[n]) <= room) used += W(text[n++]);
                return text.Substring(0, n) + "…";
            }

            static float EstimateWidth(string text, int size = 13)
            {
                float w = 0;
                foreach (char c in text) w += c > 0x2E7F ? size : size * 0.56f;
                return w;
            }

            const float PanelWidth = 640f;
            const float ColLevel = 45f;
            const float ColAp = 40f;
            const float ColWage = 65f;
            const float ColSchedules = 270f;
            const float BtnUp = 30f;
            const float BtnDown = 30f;
            const float Gaps = 6 * 8f; // 6 gaps of 8px in BeginRow(8)

            foreach (string loc in new[] { "zh", "en", "de", "fr", "es", "pl", "ru", "ja" })
            {
                var table = LoadTable(loc);

                // 1. 各列与按钮之和不超出面板宽度
                float btnMoreW = table.TryGetValue("layout.btn_more_width", out string bmw) && float.TryParse(bmw, out float fbmw) ? fbmw : 78f;
                float totalRowW = ColLevel + ColAp + ColWage + ColSchedules + BtnUp + BtnDown + btnMoreW + Gaps;
                check(totalRowW <= PanelWidth, $"{loc} 学徒页整行宽度 {totalRowW} 超出面板宽度 {PanelWidth}");

                // 2. 表头文字通过 Fit 检查不被截断 (字号 11)
                string colLevel = table.GetValueOrDefault("intercom.col_level", "");
                string colAp = table.GetValueOrDefault("intercom.col_ap", "");
                string colWage = table.GetValueOrDefault("intercom.col_wage", "");
                string colSchedules = table.GetValueOrDefault("intercom.col_schedules", "");

                Fit(colLevel, ColLevel, 11, out bool cutLevel);
                check(!cutLevel, $"{loc} intercom.col_level [{colLevel}] 在宽度 {ColLevel} 下被截断");
                Fit(colAp, ColAp, 11, out bool cutAp);
                check(!cutAp, $"{loc} intercom.col_ap [{colAp}] 在宽度 {ColAp} 下被截断");
                Fit(colWage, ColWage, 11, out bool cutWage);
                check(!cutWage, $"{loc} intercom.col_wage [{colWage}] 在宽度 {ColWage} 下被截断");
                Fit(colSchedules, ColSchedules, 11, out bool cutSched);
                check(!cutSched, $"{loc} intercom.col_schedules [{colSchedules}] 在宽度 {ColSchedules} 下被截断");

                // 3. 班次下拉短名称不超出下拉宽度 (预留 24px 箭头)
                float shiftW = table.TryGetValue("layout.intercom.shift_width", out string sw) && float.TryParse(sw, out float fsw) ? fsw : 80f;
                string shortMorning = table.GetValueOrDefault("shift.short.morning", "");
                string shortNight = table.GetValueOrDefault("shift.short.night", "");
                check(EstimateWidth(shortMorning) + 24 <= shiftW, $"{loc} shift.short.morning [{shortMorning}] 宽度超出 shift_width {shiftW}");
                check(EstimateWidth(shortNight) + 24 <= shiftW, $"{loc} shift.short.night [{shortNight}] 宽度超出 shift_width {shiftW}");
            }
        });

        test("多语言·src 里除 L.cs 外不得使用 L.IsZh", () =>
        {
            string src = Path.Combine(FindRoot(), "src");
            foreach (var f in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
            {
                string rel = f.Replace('\\', '/');
                if (rel.EndsWith("/Loc/L.cs") || rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
                check(!File.ReadAllText(f).Contains("IsZh"), $"{rel} 使用了 IsZh");
            }
        });

        test("多语言·物品名称解析安全与Translation Error防护", () =>
        {
            // 1. 模拟游戏表缺少键时返回 Translation Error 字符串，绝不能返回给上层
            string errorSerum = "Translation Error 'item_serum_red_name' in Item";
            string missingDisk = "No translation found for 'item_plant_dna_disk_name' in 'Item'";

            string resolved1 = Catalog.ResolveKindName("serum_red", k => errorSerum, "Red Serum", "serum_red");
            check(resolved1 == "Red Serum", $"缺失键时未回退到原型名: {resolved1}");
            check(!resolved1.Contains("Translation Error"), $"名字包含了 Translation Error: {resolved1}");

            string resolved2 = Catalog.ResolveKindName("plant_dna_disk", k => missingDisk, "Plant DNA Disk", "plant_dna_disk");
            check(resolved2 == "Plant DNA Disk", $"缺失键时未回退到原型名: {resolved2}");
            check(!resolved2.Contains("No translation found"), $"名字包含了 No translation found: {resolved2}");

            // 2. 原型名缺失或非法时退回 fallback
            string resolved3 = Catalog.ResolveKindName("unknown_id", k => errorSerum, null, "fallback_name");
            check(resolved3 == "fallback_name", $"原型名为空时未退回 fallback: {resolved3}");

            // 3. FakeGame 模拟验证
            string fakeSerum = FakeGame.SimulateGameItemName("serum_red", "Red Serum");
            check(fakeSerum == "牲畜类固醇血清" || fakeSerum == "Livestock Steroid Serum" || fakeSerum == "Red Serum", $"FakeGame serum_red 返回错误: {fakeSerum}");

            string fakeDisk = FakeGame.SimulateGameItemName("plant_dna_disk", "Plant DNA Disk");
            check(fakeDisk == "Plant DNA Disk", $"FakeGame plant_dna_disk 返回错误: {fakeDisk}");

            // 4. IsValidName 逻辑检验
            check(!Catalog.IsValidName(null, "key"), "null 应判定为无效");
            check(!Catalog.IsValidName("", "key"), "空串应判定为无效");
            check(!Catalog.IsValidName("key", "key"), "等于键名应判定为无效");
            check(!Catalog.IsValidName(errorSerum, "key"), "Translation Error 应判定为无效");
            check(!Catalog.IsValidName(missingDisk, "key"), "No translation found 应判定为无效");
            check(Catalog.IsValidName("Valid Item", "key"), "合法名字应判定为有效");
        });

        test("多语言·模拟器顶部布局与短班次不覆盖", () =>
        {
            const float windowWidth = 1440f;
            foreach (var loc in new[] { "zh", "en", "de", "fr", "es", "pl", "ru", "ja" })
            {
                var table = LoadTable(loc);

                // 1. 起始下拉与短班次
                float startW = table.TryGetValue("layout.sim.start_width", out string sw) && float.TryParse(sw, out float fsw) ? fsw : 90f;
                string morning = table.GetValueOrDefault("sim.start_morning", "");
                string night = table.GetValueOrDefault("sim.start_night", "");
                string shortMorning = table.GetValueOrDefault("shift.short.morning", "");
                string shortNight = table.GetValueOrDefault("shift.short.night", "");
                check(!string.IsNullOrEmpty(morning), $"[{loc}] sim.start_morning 不能为空");
                check(!string.IsNullOrEmpty(night), $"[{loc}] sim.start_night 不能为空");
                FitText(morning, startW - 24, 13, out bool cutM);
                check(!cutM, $"[{loc}] sim.start_morning [{morning}] 在下拉宽度 {startW} 下被截断");
                FitText(night, startW - 24, 13, out bool cutN);
                check(!cutN, $"[{loc}] sim.start_night [{night}] 在下拉宽度 {startW} 下被截断");
                if (!string.IsNullOrEmpty(shortMorning))
                {
                    FitText(shortMorning, startW - 24, 13, out bool cutSM);
                    check(!cutSM, $"[{loc}] shift.short.morning [{shortMorning}] 在下拉宽度 {startW} 下被截断");
                }
                if (!string.IsNullOrEmpty(shortNight))
                {
                    FitText(shortNight, startW - 24, 13, out bool cutSN);
                    check(!cutSN, $"[{loc}] shift.short.night [{shortNight}] 在下拉宽度 {startW} 下被截断");
                }

                // 2. 时长下拉与标签
                float durW = table.TryGetValue("layout.sim.duration_width", out string dw) && float.TryParse(dw, out float fdw) ? fdw : 110f;
                string durLabel = table.GetValueOrDefault("sim.duration", "");
                check(!string.IsNullOrEmpty(durLabel), $"[{loc}] sim.duration 不能为空");
                string[] runKeys = { "sim.run_1_shift", "sim.run_1_day", "sim.run_3_days", "sim.run_7_days" };
                foreach (var rk in runKeys)
                {
                    string runText = table.GetValueOrDefault(rk, "");
                    check(!string.IsNullOrEmpty(runText), $"[{loc}] {rk} 不能为空");
                    FitText(runText, durW - 24, 13, out bool cutRun);
                    check(!cutRun, $"[{loc}] {rk} [{runText}] 在下拉宽度 {durW} 下被截断");
                }

                // 3. 顶部行宽度之和不超过窗口宽度
                float schedLabelW = EstWidth(table.GetValueOrDefault("sim.schedule", ""), 13);
                float startLabelW = EstWidth(table.GetValueOrDefault("sim.start_shift", ""), 13);
                float durLabelW = EstWidth(durLabel, 13);
                float btnStartW = EstWidth(table.GetValueOrDefault("sim.btn_start", ""), 13) + 20;
                float btnResetW = EstWidth(table.GetValueOrDefault("ui.reset", ""), 13) + 20;
                float btnEditW = EstWidth(table.GetValueOrDefault("sim.btn_edit_schedule", ""), 13) + 20;
                float btnCloseW = EstWidth(table.GetValueOrDefault("ui.close", ""), 13) + 20;
                float totalW = schedLabelW + 200 + startLabelW + startW + durLabelW + durW + btnStartW + btnResetW + btnEditW + btnCloseW + 100;
                check(totalW <= windowWidth, $"[{loc}] 模拟器顶部总宽 {totalW} 超过窗口宽度 {windowWidth}");
            }
        });

        test("多语言·流程页表头和设备计数不截断", () =>
        {
            var enTable = LoadTable("en");
            foreach (var loc in new[] { "zh", "en", "de", "fr", "es", "pl", "ru", "ja" })
            {
                var table = LoadTable(loc);
                L.Use(loc, table, enTable);

                float colDevW = table.TryGetValue("layout.schedule.col_device_width", out string cdw) && float.TryParse(cdw, out float fcdw) ? fcdw : 90f;
                float colRuleW = table.TryGetValue("layout.schedule.col_rule_width", out string crw) && float.TryParse(crw, out float fcrw) ? fcrw : 90f;

                // 1. 表头列不被截断 (字号 11)
                string devHeader = table.GetValueOrDefault("schedule.col_device_header", "");
                FitText(devHeader, colDevW, 11, out bool cutDevH);
                check(!cutDevH, $"[{loc}] schedule.col_device_header [{devHeader}] 在宽度 {colDevW} 下被截断");

                string ruleHeader = table.GetValueOrDefault("schedule.col_rule_header", "");
                FitText(ruleHeader, colRuleW, 11, out bool cutRuleH);
                check(!cutRuleH, $"[{loc}] schedule.col_rule_header [{ruleHeader}] 在宽度 {colRuleW} 下被截断");

                // 2. 行文字（设备计数和任务计数）不被截断 (字号 13)
                int[] sampleCounts = { 0, 1, 2, 5, 10 };
                foreach (int n in sampleCounts)
                {
                    string devText = L.N("schedule.count_devices", n);
                    FitText(devText, colDevW, 13, out bool cutDev);
                    check(!cutDev, $"[{loc}] schedule.count_devices n={n} [{devText}] 在宽度 {colDevW} 下被截断");

                    string taskText = L.N("schedule.count_tasks", n);
                    FitText(taskText, colRuleW, 13, out bool cutTask);
                    check(!cutTask, $"[{loc}] schedule.count_tasks n={n} [{taskText}] 在宽度 {colRuleW} 下被截断");
                }
            }
            L.Use("zh", LoadTable("zh"), enTable);
        });

        test("多语言·条件分组与选择器范围标签不截断", () =>
        {
            foreach (var loc in new[] { "zh", "en", "de", "fr", "es", "pl", "ru", "ja" })
            {
                var table = LoadTable(loc);

                // 1. 条件分组下拉文字不超出下拉宽度 (预留 24px 箭头)
                float condW = table.TryGetValue("layout.cond.value_width", out string cw) && float.TryParse(cw, out float fcw) ? fcw : (loc == "zh" || loc == "ja" ? 120f : 160f);
                string[] groupKeys = { "cond.group.base", "cond.group.quality", "cond.group.ingredient", "cond.group.ratio", "cond.group.tag", "cond.group.type" };
                foreach (var gk in groupKeys)
                {
                    string gName = table.GetValueOrDefault(gk, "");
                    check(!string.IsNullOrEmpty(gName), $"[{loc}] {gk} 不能为空");
                    FitText(gName, condW - 24, 13, out bool cutG);
                    check(!cutG, $"[{loc}] {gk} [{gName}] 在宽度 {condW} 下被截断");
                }

                // 2. 选择器范围起点/连接标签不被挤压折行
                float scopeLabelW = table.TryGetValue("layout.selector.scope_label_width", out string slw) && float.TryParse(slw, out float fslw) ? fslw : 24f;
                string scopeFrom = table.GetValueOrDefault("selector.scope_from", "");
                string scopeAnd = table.GetValueOrDefault("selector.scope_and", "");
                check(EstWidth(scopeFrom, 13) <= scopeLabelW, $"[{loc}] selector.scope_from [{scopeFrom}] 宽度超出 {scopeLabelW}");
                check(EstWidth(scopeAnd, 13) <= scopeLabelW, $"[{loc}] selector.scope_and [{scopeAnd}] 宽度超出 {scopeLabelW}");

                // 3. selector.scope_all_inside 在下拉内不被截断 (下拉宽度 260px)
                string allInside = table.GetValueOrDefault("selector.scope_all_inside", "");
                check(!string.IsNullOrEmpty(allInside), $"[{loc}] selector.scope_all_inside 不能为空");
                FitText(allInside, 260 - 24, 13, out bool cutInside);
                check(!cutInside, $"[{loc}] selector.scope_all_inside [{allInside}] 在下拉宽度 260 下被截断");
            }
        });

        test("多语言·物品原型与液体及Mod物品解析无汉字与无回退id", () =>
        {
            var enTable = LoadTable("en");
            L.Use("en", enTable, enTable);

            // 1. “没有名字键的物品”：serum_red / serum_green，以及游戏和 mod 表里都没有名字键的物品（test_no_key_item）
            // 存档中即使带有中文残留（"牲畜类固醇血清"），在 en 模式下也绝不返回汉字或原始 id
            var noKeyItems = new[]
            {
                ("serum_red", "Livestock Steroid Serum", "牲畜类固醇血清"),
                ("serum_green", "Livestock Immunity Serum", "牲畜免疫血清"),
                ("test_no_key_item", "Plant DNA Disk", "植物DNA芯片"),
                ("rat", "Rat", "老鼠")
            };
            foreach (var (id, expected, staleZh) in noKeyItems)
            {
                string res = FakeGame.SimulateGameItemName(id, id == "test_no_key_item" ? expected : null, staleZh);
                check(!res.Any(c => c >= 0x4E00 && c <= 0x9FFF), $"[{id}] 英文模式下包含汉字: {res}");
                check(res != id, $"[{id}] 退回到了原始 id: {res}");
                check(res == expected, $"[{id}] 期望解析为 [{expected}]，实际为 [{res}]");
            }

            // 2. “液体”：water, livestock_steroid, livestock_immunity, acid
            var liquids = new[]
            {
                ("water", "Water", "水"),
                ("livestock_steroid", "Livestock Steroid Serum", "牲畜类固醇血清"),
                ("livestock_immunity", "Livestock Immunity Serum", "牲畜免疫血清"),
                ("acid", "Acid", "酸")
            };
            foreach (var (id, expected, staleZh) in liquids)
            {
                string res = FakeGame.SimulateGameItemName(id, null, staleZh);
                check(!res.Any(c => c >= 0x4E00 && c <= 0x9FFF), $"液体 [{id}] 英文模式下包含汉字: {res}");
                check(res != id, $"液体 [{id}] 退回到了原始 id: {res}");
                check(res == expected, $"液体 [{id}] 期望解析为 [{expected}]，实际为 [{res}]");
            }

            // 3. “mod 物品”：automod_handbook, automod_intercom
            var modItems = new[]
            {
                ("probably_automated_handbook", "Apprentice Handbook", "学徒手册"),
                ("probably_automated_intercom", "Apprentice Intercom", "学徒对讲机"),
                ("probably_automated_daily", "Apprentice Daily Report", "工作日报")
            };
            foreach (var (id, expected, staleZh) in modItems)
            {
                string res = FakeGame.SimulateGameItemName(id, null, staleZh);
                check(!res.Any(c => c >= 0x4E00 && c <= 0x9FFF), $"Mod物品 [{id}] 英文模式下包含汉字: {res}");
                check(res != id, $"Mod物品 [{id}] 退回到了原始 id: {res}");
                check(res == expected, $"Mod物品 [{id}] 期望解析为 [{expected}]，实际为 [{res}]");
            }

            // 4. 全量物品原型在 8 种语言下检查：无汉字（zh 除外），无退回 id
            // 需要游戏物品表的导出目录（环境变量 AUTOMOD_GAMETABLES，下有 <语言>/Item.tsv 等）；没有就跳过这一项
            string gtRoot = Environment.GetEnvironmentVariable("AUTOMOD_GAMETABLES") ?? "";
            string enItemTsv = Path.Combine(gtRoot, "en", "Item.tsv");
            if (gtRoot != "" && File.Exists(enItemTsv))
            {
                var allItemIds = new HashSet<string>();
                foreach (var line in File.ReadAllLines(enItemTsv))
                {
                    var parts = line.Split('\t');
                    if (parts.Length >= 2 && parts[0].StartsWith("item_") && parts[0].EndsWith("_name"))
                    {
                        allItemIds.Add(parts[0][5..^5]);
                    }
                }
                // 加入特殊项
                allItemIds.Add("serum_red");
                allItemIds.Add("serum_green");
                allItemIds.Add("probably_automated_handbook");
                allItemIds.Add("probably_automated_intercom");
                allItemIds.Add("probably_automated_daily");
                allItemIds.Add("rat");
                allItemIds.Add("water");
                allItemIds.Add("livestock_steroid");

                foreach (var loc in new[] { "en", "de", "ru", "fr", "es", "pl", "ja", "zh" })
                {
                    var curTable = LoadTable(loc);
                    L.Use(loc, curTable, enTable);

                    var itemDict = new Dictionary<string, string>();
                    string itPath = Path.Combine(gtRoot, loc, "Item.tsv");
                    if (File.Exists(itPath))
                        foreach (var line in File.ReadAllLines(itPath))
                        {
                            var parts = line.Split('\t');
                            if (parts.Length >= 2) itemDict[parts[0]] = parts[1];
                        }

                    var mechDict = new Dictionary<string, string>();
                    string mtPath = Path.Combine(gtRoot, loc, "Mechanic.tsv");
                    if (File.Exists(mtPath))
                        foreach (var line in File.ReadAllLines(mtPath))
                        {
                            var parts = line.Split('\t');
                            if (parts.Length >= 2) mechDict[parts[0]] = parts[1];
                        }

                    foreach (var itemId in allItemIds)
                    {
                        string name = Catalog.ResolveKindName(
                            itemId,
                            k => itemDict.GetValueOrDefault(k),
                            null,
                            "过时中文",
                            k => mechDict.GetValueOrDefault(k)
                        );
                        check(!string.IsNullOrEmpty(name), $"[{loc}] {itemId} 解析结果为空");
                        check(name != itemId, $"[{loc}] {itemId} 退回到了原始 id: {name}");
                        if (loc != "zh" && loc != "ja")
                        {
                            check(!name.Any(c => c >= 0x4E00 && c <= 0x9FFF), $"[{loc}] {itemId} 解析结果包含汉字: {name}");
                        }
                    }
                }
            }

            L.Use("zh", LoadTable("zh"), enTable);
        });

        test("多语言·报告页完成数量与详情按钮宽度不截断", () =>
        {
            var enTable = LoadTable("en");
            foreach (var loc in new[] { "zh", "en", "de", "fr", "es", "pl", "ru", "ja" })
            {
                var table = LoadTable(loc);
                L.Use(loc, table, enTable);

                // 1. 列宽检查：第一列完成数量（含表头与最长形式 n=12）不被截断 (Table.Fit 算法)
                float colDoneW = table.TryGetValue("layout.intercom.col_done_width", out string cdw) && float.TryParse(cdw, out float fcdw) ? fcdw : 80f;

                // 表头 (字号 11)
                string colHeader = table.GetValueOrDefault("intercom.col_done", "");
                check(!string.IsNullOrEmpty(colHeader), $"[{loc}] intercom.col_done 不能为空");
                FitText(colHeader, colDoneW, 11, out bool cutHeader);
                check(!cutHeader, $"[{loc}] intercom.col_done [{colHeader}] 在宽度 {colDoneW} 下被截断");

                // 行内数量：n=1 与最长形式 n=12（字号 13）
                int[] sampleCounts = { 1, 2, 5, 12 };
                foreach (int n in sampleCounts)
                {
                    string doneText = L.N("intercom.done_items", n);
                    FitText(doneText, colDoneW, 13, out bool cutDone);
                    check(!cutDone, $"[{loc}] intercom.done_items n={n} [{doneText}] 在列宽 {colDoneW} 下被截断");
                }

                // 2. 详情按钮检查：intercom.btn_details 文字放得进按钮宽 (layout.btn_more_width)，且与流程页 table.btn_details 一致
                float btnMoreW = table.TryGetValue("layout.btn_more_width", out string bmw) && float.TryParse(bmw, out float fbmw) ? fbmw : 78f;
                string intercomDetails = table.GetValueOrDefault("intercom.btn_details", "");
                string tableDetails = table.GetValueOrDefault("table.btn_details", "");
                check(!string.IsNullOrEmpty(intercomDetails), $"[{loc}] intercom.btn_details 不能为空");
                check(intercomDetails == tableDetails, $"[{loc}] intercom.btn_details [{intercomDetails}] 应与 table.btn_details [{tableDetails}] 一致");
                check(EstWidth(intercomDetails, 13) + 16 <= btnMoreW, $"[{loc}] intercom.btn_details [{intercomDetails}] 宽度超出 btn_more_width {btnMoreW}");
                FitText(intercomDetails, btnMoreW - 16, 13, out bool cutBtn);
                check(!cutBtn, $"[{loc}] intercom.btn_details [{intercomDetails}] 在按钮可用宽度 {btnMoreW - 16} 下被截断");
            }

            L.Use("zh", LoadTable("zh"), enTable);
        });

        test("多语言·报告页状态文字最长形式放得进状态格宽度", () =>
        {
            var enTable = LoadTable("en");
            foreach (var loc in new[] { "zh", "en", "de", "fr", "es", "pl", "ru", "ja" })
            {
                var table = LoadTable(loc);
                L.Use(loc, table, enTable);

                float statusW = table.TryGetValue("layout.intercom.status_width", out string sw) && float.TryParse(sw, out float fsw) ? fsw : 160f;

                int[] sampleCounts = { 1, 2, 5, 12 };
                foreach (int n in sampleCounts)
                {
                    string issueText = L.T("intercom.status_issues", n);
                    check(!string.IsNullOrEmpty(issueText), $"[{loc}] intercom.status_issues n={n} 不能为空");
                    FitText(issueText, statusW, 13, out bool cutIssue);
                    check(!cutIssue, $"[{loc}] intercom.status_issues n={n} [{issueText}] 在状态格宽度 {statusW} 下被截断");

                    string deferredText = L.T("intercom.status_deferred", n);
                    check(!string.IsNullOrEmpty(deferredText), $"[{loc}] intercom.status_deferred n={n} 不能为空");
                    FitText(deferredText, statusW, 13, out bool cutDeferred);
                    check(!cutDeferred, $"[{loc}] intercom.status_deferred n={n} [{deferredText}] 在状态格宽度 {statusW} 下被截断");
                }
            }

            L.Use("zh", LoadTable("zh"), enTable);
        });

        test("多语言·工作日报数字格说明文字宽度与长度检查", () =>
        {
            var statKeys = new[] { "daily.stat.done", "daily.stat.ap", "daily.stat.wage", "daily.stat.problems" };
            var sectionKeys = new[] { "daily.section.apprentices", "daily.section.flows", "daily.section.problems" };
            var locales = new[] { "zh", "en", "ru", "de", "fr", "es", "pl", "ja" };

            foreach (var loc in locales)
            {
                var table = LoadTable(loc);
                bool isZh = loc == "zh";

                foreach (var sk in sectionKeys)
                {
                    check(table.TryGetValue(sk, out var sval) && !string.IsNullOrEmpty(sval), $"[{loc}] 缺少横幅/小标题键 {sk}");
                }
                if (isZh)
                {
                    check(table["daily.section.apprentices"] == "学徒", "[zh] daily.section.apprentices 应为 学徒");
                }

                foreach (var k in statKeys)
                {
                    check(table.TryGetValue(k, out var val) && !string.IsNullOrEmpty(val), $"[{loc}] 缺少键 {k}");
                    if (isZh)
                    {
                        int cleanLen = val.Replace(" ", "").Length;
                        check(cleanLen is >= 2 and <= 4, $"[{loc}] {k} [{val}] 中文字数应在 2–4 个字（当前 {cleanLen}）");
                    }
                    else
                    {
                        check(val.Length <= 12, $"[{loc}] {k} [{val}] 字符数应 <= 12（当前 {val.Length}）");
                    }

                    FitText(val, 120f, 13, out bool cut);
                    check(!cut, $"[{loc}] {k} [{val}] 在 13 号字、格宽 120 下放不下被截断");
                }
            }
        });
    }
}
