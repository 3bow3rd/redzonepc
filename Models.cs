using System.Globalization;
using System.Text.Json.Nodes;

namespace RedZoneClub;

public class DrinkDef { public string N = ""; public double P; }
public class DrinkQty { public string N = ""; public double P; public int Q; }
public class Rates { public double P2 = 10000, P4 = 15000; }

/// <summary>جهاز: On=false يعني متاح. نفس بنية البيانات في نسخة أندرويد والنسخة القديمة.</summary>
public class Dev
{
    public bool On; public long Start; public int Mode = 2; public string Type = "open";
    public List<DrinkQty> Drinks = new();
    public double Mins, Amount, Acc; public long Seg, T2, T4; public int M0; public bool Cont;
}

public class AppState
{
    public Rates Rates = new();
    public List<DrinkDef> Drinks = new();
    public List<Dev> Devices = new();
    public long Ts;
}

public class Calc
{
    public long El; public double Rem, Tc, Dr, Total, Pr, T2, T4, C2, C4; public bool Kind, Reached;
}

public class LogEntry
{
    public string Id = ""; public int Dev, Mode, M0; public string Type = "open";
    public long Start, End; public double T2, T4, C2, C4, TimeCost, DrinkTotal, Total;
    public List<DrinkQty> Drinks = new(); public string Day = "";
}

public static class M
{
    public static long JsRound(double x) => (long)Math.Floor(x + 0.5);

    public static AppState Defaults() => new()
    {
        Rates = new Rates(),
        Drinks = new() {
            new() { N = "شاي", P = 2000 }, new() { N = "قهوة", P = 3000 }, new() { N = "عصير", P = 5000 },
            new() { N = "مياه", P = 1000 }, new() { N = "كولا", P = 3000 } },
        Devices = Enumerable.Range(0, 4).Select(_ => new Dev()).ToList()
    };

    public static double RateOf(Dev d, Rates r) => (d.Mode == 4 ? r.P4 : r.P2) / 3_600_000.0;

    /// <summary>أصغر وحدة للمبلغ: 500 ل.س. أي مبلغ وقت يُقرَّب للأعلى إلى مضاعفات 500 (والحد الأدنى 500).</summary>
    public const double Step = 500;
    public static double Up(double x) => Math.Max(Step, Math.Ceiling(x / Step - 1e-6) * Step);

    /// <summary>نفس معادلات الحساب في النسخة الأصلية تماماً.</summary>
    public static Calc Compute(Dev d, long now, Rates rates)
    {
        double r = RateOf(d, rates);
        long baseT = d.Seg != 0 ? d.Seg : d.Start;
        double raw = d.Acc + (now - baseT) * r;
        long el = now - d.Start;
        bool kind = d.Type != "open";
        double rem = 0, tc = raw; bool reached = false;
        if (d.Type == "timed") { rem = d.Mins * 60000.0 - el; reached = rem <= 0; tc = raw + Math.Max(0, rem) * r; }
        else if (d.Type == "amount") { rem = (d.Amount - raw) / r; reached = rem <= 0; }
        tc = d.Type == "amount" ? Math.Max(d.Amount, Up(raw)) : Up(tc);
        double t2 = d.T2, t4 = d.T4;
        double sg = (now - baseT) + (kind && rem > 0 ? rem : 0);
        if (d.Mode == 4) t4 += sg; else t2 += sg;
        double c2 = Math.Min(tc, JsRound(t2 * rates.P2 / 1800000000.0) * Step);
        double c4 = tc - c2;
        if (t4 <= 0) { c2 = tc; c4 = 0; } else if (t2 <= 0) { c2 = 0; c4 = tc; }
        double dr = d.Drinks.Sum(x => x.P * x.Q);
        double pr = d.Type == "timed" ? el / (d.Mins * 60000.0) : d.Type == "amount" ? raw / d.Amount : 0;
        return new Calc { El = el, Rem = rem, Kind = kind, Reached = reached, Tc = tc, Dr = dr, Total = tc + dr, Pr = pr, T2 = t2, T4 = t4, C2 = c2, C4 = c4 };
    }

    public static LogEntry EntryFrom(Dev d, int i, long end, Rates rates)
    {
        var c = Compute(d, end, rates);
        return new LogEntry
        {
            Id = $"l{end}_{i}", Dev = i + 1, Mode = d.Mode, M0 = d.M0 != 0 ? d.M0 : d.Mode, Type = d.Type,
            Start = d.Start, End = end, T2 = c.T2, T4 = c.T4, C2 = c.C2, C4 = c.C4, TimeCost = c.Tc,
            Drinks = d.Drinks.Select(x => new DrinkQty { N = x.N, P = x.P, Q = x.Q }).ToList(),
            DrinkTotal = c.Dr, Total = c.Total, Day = DKey(end)
        };
    }

    /* ---------- JSON ---------- */
    public static double D(JsonNode? n, double def = 0)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<long>(out var l)) return l;
            if (v.TryGetValue<bool>(out var b)) return b ? 1 : 0;
            if (v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return x;
        }
        return def;
    }
    public static string Str(JsonNode? n, string def = "")
    {
        if (n is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        return def;
    }

    static JsonArray DrinksJson(List<DrinkQty> l)
    {
        var a = new JsonArray();
        foreach (var x in l) a.Add(new JsonObject { ["n"] = x.N, ["p"] = x.P, ["q"] = x.Q });
        return a;
    }
    static List<DrinkQty> ParseDrinks(JsonNode? n)
    {
        var res = new List<DrinkQty>();
        IEnumerable<JsonNode?> items = n is JsonArray a ? a : n is JsonObject o ? o.Select(kv => kv.Value) : Enumerable.Empty<JsonNode?>();
        foreach (var x in items)
            if (x is JsonObject ob) res.Add(new DrinkQty { N = Str(ob["n"]), P = D(ob["p"]), Q = (int)D(ob["q"], 1) });
        return res;
    }

    public static JsonObject ToJson(this Dev d)
    {
        var o = new JsonObject { ["on"] = d.On };
        if (!d.On) return o;
        o["start"] = d.Start; o["mode"] = d.Mode; o["type"] = d.Type; o["drinks"] = DrinksJson(d.Drinks);
        if (d.Type == "timed") o["mins"] = d.Mins;
        if (d.Type == "amount") o["amount"] = d.Amount;
        if (d.Acc != 0) o["acc"] = d.Acc;
        if (d.Seg != 0) o["seg"] = d.Seg;
        if (d.T2 != 0) o["t2"] = d.T2;
        if (d.T4 != 0) o["t4"] = d.T4;
        if (d.M0 != 0) o["m0"] = d.M0;
        if (d.Cont) o["cont"] = 1;
        return o;
    }

    public static Dev DevFrom(JsonObject o) => new()
    {
        On = D(o["on"]) != 0, Start = (long)D(o["start"]), Mode = (int)D(o["mode"], 2),
        Type = Str(o["type"], "open") is { Length: > 0 } t ? t : "open",
        Drinks = ParseDrinks(o["drinks"]), Mins = D(o["mins"]), Amount = D(o["amount"]), Acc = D(o["acc"]),
        Seg = (long)D(o["seg"]), T2 = (long)D(o["t2"]), T4 = (long)D(o["t4"]), M0 = (int)D(o["m0"]), Cont = D(o["cont"]) != 0
    };

    public static JsonObject ToJson(this AppState s)
    {
        var ds = new JsonArray(); foreach (var x in s.Drinks) ds.Add(new JsonObject { ["n"] = x.N, ["p"] = x.P });
        var dv = new JsonArray(); foreach (var x in s.Devices) dv.Add(x.ToJson());
        return new JsonObject
        {
            ["rates"] = new JsonObject { ["p2"] = s.Rates.P2, ["p4"] = s.Rates.P4 },
            ["drinks"] = ds, ["devices"] = dv, ["ts"] = s.Ts
        };
    }

    public static AppState StateFrom(JsonObject o)
    {
        var def = Defaults();
        if (o["rates"] is JsonObject r) def.Rates = new Rates { P2 = D(r["p2"], 10000), P4 = D(r["p4"], 15000) };
        if (o["drinks"] is JsonArray da)
            def.Drinks = da.OfType<JsonObject>().Select(x => new DrinkDef { N = Str(x["n"]), P = D(x["p"]) }).ToList();
        if (o["devices"] is JsonArray va && va.Count > 0)
            def.Devices = va.Select(x => x is JsonObject xo ? DevFrom(xo) : new Dev()).ToList();
        def.Ts = (long)D(o["ts"]);
        return def;
    }

    public static JsonObject ToJson(this LogEntry e) => new()
    {
        ["id"] = e.Id, ["dev"] = e.Dev, ["mode"] = e.Mode, ["m0"] = e.M0, ["type"] = e.Type,
        ["start"] = e.Start, ["end"] = e.End, ["t2"] = e.T2, ["t4"] = e.T4, ["c2"] = e.C2, ["c4"] = e.C4,
        ["timeCost"] = e.TimeCost, ["drinks"] = DrinksJson(e.Drinks), ["drinkTotal"] = e.DrinkTotal,
        ["total"] = e.Total, ["day"] = e.Day
    };

    public static LogEntry LogFrom(JsonObject o)
    {
        long end = (long)D(o["end"]); int mode = (int)D(o["mode"], 2);
        return new LogEntry
        {
            Id = Str(o["id"]), Dev = (int)D(o["dev"]), Mode = mode, M0 = (int)D(o["m0"], mode),
            Type = Str(o["type"], "open"), Start = (long)D(o["start"]), End = end,
            T2 = D(o["t2"]), T4 = D(o["t4"]), C2 = D(o["c2"]), C4 = D(o["c4"]), TimeCost = D(o["timeCost"]),
            Drinks = ParseDrinks(o["drinks"]), DrinkTotal = D(o["drinkTotal"]), Total = D(o["total"]),
            Day = Str(o["day"]) is { Length: > 0 } dd ? dd : DKey(end)
        };
    }

    /* ---------- تنسيقات ---------- */
    static DateTime Loc(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
    public static long ToMs(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();
    public static string DKey(long t) => Loc(t).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string Tm(long t) => "\u200E" + Loc(t).ToString("h:mm tt", CultureInfo.InvariantCulture) + "\u200E";
    public static string Hms(double ms)
    {
        long s = Math.Max(0, (long)Math.Floor(ms / 1000.0));
        return $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}";
    }
    public static string Fmt(double n) => JsRound(n).ToString("N0", CultureInfo.InvariantCulture) + " ل.س";
    public static string Dur(double ms)
    {
        long s = JsRound(ms / 1000.0);
        if (s < 60) return $"{s} ث";
        long m = JsRound(ms / 60000.0), h = m / 60;
        return (h > 0 ? $"{h} س " : "") + (m % 60 != 0 || h == 0 ? $"{m % 60} د" : "");
    }
    public static string PinHash(string p) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"rz|{p}|club"))).ToLowerInvariant();

    public static string InvText(LogEntry e)
    {
        var sb = new System.Text.StringBuilder("Red Zone Club\n");
        bool sale = e.Type == "sale";
        if (sale) sb.Append($"فاتورة — بيع مباشر\n{DKey(e.End)} — {Tm(e.End)}\n");
        else sb.Append($"فاتورة — جهاز {e.Dev}\n{DKey(e.Start)} — من {Tm(e.Start)} إلى {Tm(e.End)}\n");
        if (!sale)
        {
            sb.Append("\nوقت اللعب\n");
            if (e.T2 > 0 || e.T4 > 0)
            {
                if (e.T2 > 0) sb.Append($"لاعبان ({Dur(e.T2)}) : {Fmt(e.C2)}\n");
                if (e.T4 > 0) sb.Append($"٤ لاعبين ({Dur(e.T4)}) : {Fmt(e.C4)}\n");
                sb.Append($"إجمالي وقت اللعب ({Dur(e.T2 + e.T4)}) : {Fmt(e.TimeCost)}\n");
            }
            else sb.Append($"المدة ({Dur(e.End - e.Start)}) : {Fmt(e.TimeCost)}\n");
        }
        sb.Append("\nالمشروبات\n");
        if (e.Drinks.Count == 0 && e.DrinkTotal == 0) sb.Append("لا توجد مشروبات\n");
        foreach (var x in e.Drinks) sb.Append($"{x.N} × {x.Q} ({Fmt(x.P)}) : {Fmt(x.P * x.Q)}\n");
        if (e.DrinkTotal > 0 || e.Drinks.Count > 0) sb.Append($"مجموع المشروبات : {Fmt(e.DrinkTotal)}\n");
        sb.Append($"\nالإجمالي النهائي: {Fmt(e.Total)}");
        return sb.ToString();
    }
    public static string TmPlain(long t) => Loc(t).ToString("h:mm tt", CultureInfo.InvariantCulture);

    public static string ModeName(LogEntry e)
    {
        if (e.Type == "sale") return "—";
        string N(int m) => m == 4 ? "٤ لاعبين" : "لاعبان";
        return e.M0 != e.Mode && e.M0 != 0 ? N(e.M0) + " ← " + N(e.Mode) : N(e.Mode);
    }

    /* ---------- تصدير Excel ---------- */
    public static byte[] InvoiceXlsx(LogEntry e)
    {
        bool sale = e.Type == "sale";
        var s = new XSheet("الفاتورة", new[] { 30.0, 14, 10, 16, 18 });
        s.Add(X.T("Red Zone Club", X.Title));
        s.Add(X.T(sale ? "فاتورة — بيع مباشر" : $"فاتورة — جهاز {e.Dev}", X.Plain));
        s.Add(X.T("التاريخ", X.Plain), X.T(DKey(e.End), X.Plain));
        if (sale) s.Add(X.T("الوقت", X.Plain), X.T(TmPlain(e.End), X.Plain));
        else { s.Add(X.T("من", X.Plain), X.T(TmPlain(e.Start), X.Plain)); s.Add(X.T("إلى", X.Plain), X.T(TmPlain(e.End), X.Plain)); }
        s.Add();
        s.Add(X.T("البند", X.Head), X.T("المدة (دقيقة)", X.Head), X.T("الكمية", X.Head), X.T("سعر الوحدة", X.Head), X.T("المبلغ", X.Head));
        if (!sale)
        {
            if (e.T2 > 0 || e.T4 > 0)
            {
                if (e.T2 > 0) s.Add(X.T("لاعبان"), X.N(JsRound(e.T2 / 60000.0)), X.T(""), X.T(""), X.N(e.C2));
                if (e.T4 > 0) s.Add(X.T("٤ لاعبين"), X.N(JsRound(e.T4 / 60000.0)), X.T(""), X.T(""), X.N(e.C4));
            }
            else s.Add(X.T("وقت اللعب"), X.N(JsRound((e.End - e.Start) / 60000.0)), X.T(""), X.T(""), X.N(e.TimeCost));
        }
        foreach (var x in e.Drinks) s.Add(X.T(x.N), X.T(""), X.N(x.Q), X.N(x.P), X.N(x.P * x.Q));
        if (!sale) s.Add(X.T("مجموع وقت اللعب", X.TotL), X.T("", X.TotL), X.T("", X.TotL), X.T("", X.TotL), X.N(e.TimeCost, X.TotN));
        s.Add(X.T("مجموع المشروبات", X.TotL), X.T("", X.TotL), X.T("", X.TotL), X.T("", X.TotL), X.N(e.DrinkTotal, X.TotN));
        s.Add(X.T("الإجمالي النهائي (ل.س)", X.TotL), X.T("", X.TotL), X.T("", X.TotL), X.T("", X.TotL), X.N(e.Total, X.TotN));
        return X.Build(new List<XSheet> { s });
    }

    public static byte[] LogXlsx(List<LogEntry> logs)
    {
        var all = logs.OrderBy(x => x.End).ToList();
        var s1 = new XSheet("السجل", new[] { 13.0, 12, 9, 18, 11, 11, 11, 14, 34, 14, 15 }, true);
        s1.Add(X.T("التاريخ", X.Head), X.T("النوع", X.Head), X.T("الجهاز", X.Head), X.T("نظام اللعب", X.Head), X.T("البدء", X.Head), X.T("الانتهاء", X.Head),
               X.T("المدة (د)", X.Head), X.T("مبلغ اللعب", X.Head), X.T("المشروبات", X.Head), X.T("مبلغ المشروبات", X.Head), X.T("الإجمالي", X.Head));
        foreach (var e in all)
        {
            bool sale = e.Type == "sale";
            s1.Add(X.T(e.Day), X.T(sale ? "بيع مباشر" : "جلسة"), sale ? X.T("—") : X.N(e.Dev), X.T(ModeName(e)),
                   X.T(sale ? "" : TmPlain(e.Start)), X.T(TmPlain(e.End)), sale ? X.T("") : X.N(JsRound((e.End - e.Start) / 60000.0)),
                   X.N(e.TimeCost), X.T(string.Join("، ", e.Drinks.Select(d => $"{d.N}×{d.Q}"))), X.N(e.DrinkTotal), X.N(e.Total));
        }
        s1.Add(X.T("الإجمالي", X.TotL), X.T("", X.TotL), X.T("", X.TotL), X.T("", X.TotL), X.T("", X.TotL), X.T("", X.TotL), X.T("", X.TotL),
               X.N(all.Sum(x => x.TimeCost), X.TotN), X.T("", X.TotL), X.N(all.Sum(x => x.DrinkTotal), X.TotN), X.N(all.Sum(x => x.Total), X.TotN));

        var s2 = new XSheet("ملخص الأيام", new[] { 14.0, 12, 16, 16, 16 }, true);
        s2.Add(X.T("اليوم", X.Head), X.T("عدد العمليات", X.Head), X.T("مبلغ اللعب", X.Head), X.T("المشروبات", X.Head), X.T("الإجمالي", X.Head));
        foreach (var g in all.GroupBy(x => x.Day).OrderByDescending(k => k.Key))
            s2.Add(X.T(g.Key), X.N(g.Count()), X.N(g.Sum(x => x.TimeCost)), X.N(g.Sum(x => x.DrinkTotal)), X.N(g.Sum(x => x.Total)));
        s2.Add(X.T("الإجمالي", X.TotL), X.N(all.Count, X.TotN), X.N(all.Sum(x => x.TimeCost), X.TotN), X.N(all.Sum(x => x.DrinkTotal), X.TotN), X.N(all.Sum(x => x.Total), X.TotN));

        var s3 = new XSheet("المشروبات", new[] { 24.0, 12, 16 }, true);
        s3.Add(X.T("المشروب", X.Head), X.T("الكمية المباعة", X.Head), X.T("المبلغ", X.Head));
        foreach (var g in all.SelectMany(x => x.Drinks).GroupBy(d => d.N).OrderByDescending(k => k.Sum(q => q.Q)))
            s3.Add(X.T(g.Key), X.N(g.Sum(q => q.Q)), X.N(g.Sum(q => q.P * q.Q)));
        return X.Build(new List<XSheet> { s1, s2, s3 });
    }
}
