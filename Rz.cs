using System.IO;
using System.Media;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace RedZoneClub;

public record RtCfg(string Url, string Sec);
public class Form { public int Mode = 2; public string Type = "open"; public string V = ""; }
public record Pend(string M, string P, string? V);

/// <summary>حالة التطبيق ومنطقه والمزامنة (مقابل Rz.kt في نسخة أندرويد).</summary>
public static class Rz
{
    public static AppState S = M.Defaults();
    public static List<LogEntry> Logs = new();
    public static bool Live, Cloud, SetOK;
    public static RtCfg? Rt;
    public static string? LockHash;
    public static string ToneId = "alarm", CustomPath = "", CustomName = "";
    public static int? Ringing;
    public static long Offset;
    public static Dictionary<int, Form> Forms = new();
    static readonly HashSet<long> dismissed = new();
    static long alertSeen;
    /// <summary>تنبيه أمني وارد من الجهاز الآخر (تعرضه الواجهة)</summary>
    public static Action<string> AlertFn = _ => { };
    public static string DevName => Environment.MachineName;
    static List<Pend> pend = new();
    static string devId = "d" + Guid.NewGuid().ToString("N")[..6];

    /// <summary>تغيّر هيكلي: أعد رسم الواجهة</summary>
    public static event Action? Changed;
    /// <summary>تغيّر خفيف: حالة المزامنة/المنبه</summary>
    public static event Action? Soft;

    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Offset;
    public static void UI(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);
    public static Action<string> ToastFn = _ => { };
    public static void Toast(string m) => UI(() => ToastFn(m));

    /* ---------------- حفظ محلي ---------------- */
    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RedZoneClub");
    static string FilePath => Path.Combine(Dir, "data.json");

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var o = JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
            if (o["state"] is JsonObject so) S = M.StateFrom(so);
            if (o["logs"] is JsonArray la) Logs = la.OfType<JsonObject>().Select(M.LogFrom).ToList();
            ToneId = M.Str(o["tone"], "alarm"); CustomPath = M.Str(o["customPath"]); CustomName = M.Str(o["customName"]);
            LockHash = M.Str(o["lock"]) is { Length: > 0 } h ? h : null;
            if (o["rt"] is JsonObject r) Rt = new RtCfg(M.Str(r["url"]), M.Str(r["sec"]));
            Offset = (long)M.D(o["off"]);
            alertSeen = (long)M.D(o["aseen"]);
            if (M.Str(o["id"]) is { Length: > 0 } id) devId = id;
            if (o["pend"] is JsonArray pa) pend = pa.OfType<JsonObject>().Select(p => new Pend(M.Str(p["m"]), M.Str(p["p"]), M.Str(p["v"]) is { Length: > 0 } v ? v : null)).ToList();
        }
        catch { }
        Cloud = Rt != null;
    }

    static readonly object saveLock = new();
    static void SaveAll()
    {
        try
        {
            var logs = new JsonArray(); foreach (var l in Logs.ToList()) logs.Add(l.ToJson());
            var pa = new JsonArray(); foreach (var p in pend.ToList()) pa.Add(new JsonObject { ["m"] = p.M, ["p"] = p.P, ["v"] = p.V ?? "" });
            var o = new JsonObject
            {
                ["state"] = S.ToJson(), ["logs"] = logs, ["tone"] = ToneId, ["customPath"] = CustomPath, ["customName"] = CustomName,
                ["lock"] = LockHash ?? "", ["off"] = Offset, ["aseen"] = alertSeen, ["id"] = devId, ["pend"] = pa
            };
            if (Rt != null) o["rt"] = new JsonObject { ["url"] = Rt.Url, ["sec"] = Rt.Sec };
            lock (saveLock) { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, o.ToJsonString()); }
        }
        catch { }
    }

    public static void SaveS()
    {
        S.Ts = Now(); SaveAll();
        if (Cloud) RtWrite("PUT", "lounge/state", new JsonObject { ["ts"] = S.Ts, ["json"] = S.ToJson().ToJsonString() }.ToJsonString());
        Changed?.Invoke();
    }

    /* ---------------- المنبه ---------------- */
    public static readonly (string Id, string Name)[] Tones =
    {
        ("alarm", "المنبه الكلاسيكي"), ("tone_bell", "جرس"), ("tone_chime", "رنين"), ("tone_digital", "ديجيتال"),
        ("tone_siren", "صفارة"), ("tone_soft", "نغمة هادئة"), ("tone_ring", "رنين هاتف")
    };
    static SoundPlayer? sp; static MediaPlayer? mp; static bool mpLoop;

    public static void Play(string id, bool loop)
    {
        StopSound();
        try
        {
            if (id == "custom" && File.Exists(CustomPath))
            {
                mp = new MediaPlayer(); mpLoop = loop;
                var p = mp; p.MediaEnded += (_, _) => { if (mpLoop) { p.Position = TimeSpan.Zero; p.Play(); } };
                p.Open(new Uri(CustomPath)); p.Play();
            }
            else
            {
                var st = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{id}.wav")).Stream;
                sp = new SoundPlayer(st);
                if (loop) sp.PlayLooping(); else sp.Play();
            }
        }
        catch { }
    }
    public static void StopSound() { try { sp?.Stop(); mpLoop = false; mp?.Stop(); mp?.Close(); } catch { } sp = null; mp = null; }

    public static void SelTone(string id) { ToneId = id; SaveAll(); Play(id, false); }
    public static void AddCustomTone(string src)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var dst = Path.Combine(Dir, "custom_tone" + Path.GetExtension(src));
            File.Copy(src, dst, true);
            CustomPath = dst; CustomName = Path.GetFileName(src); SelTone("custom");
        }
        catch { Toast("تعذر إضافة النغمة"); }
    }

    [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO f);
    [StructLayout(LayoutKind.Sequential)] struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
    public static void Flash()
    {
        try
        {
            var w = Application.Current.MainWindow; if (w == null) return;
            var f = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = new WindowInteropHelper(w).Handle, dwFlags = 3 | 12, uCount = uint.MaxValue };
            FlashWindowEx(ref f);
        }
        catch { }
    }

    public static void Tick()
    {
        long n = Now();
        for (int i = 0; i < S.Devices.Count; i++)
        {
            var d = S.Devices[i];
            if (d.On && Ringing == null && !d.Cont && !dismissed.Contains(d.Start) && M.Compute(d, n, S.Rates).Reached)
            {
                Ringing = i; Play(ToneId, true); Flash(); Soft?.Invoke();
            }
        }
        if (Ringing is int r)
        {
            var d = r < S.Devices.Count ? S.Devices[r] : null;
            if (d == null || !d.On || d.Cont) StopAlarm();
        }
    }
    public static void StopAlarm() { Ringing = null; StopSound(); Soft?.Invoke(); }
    public static void Dismiss(int i) { if (i < S.Devices.Count) dismissed.Add(S.Devices[i].Start); if (Ringing == i) StopAlarm(); }
    public static void Continue(int i) { S.Devices[i].Cont = true; if (Ringing == i) StopAlarm(); SaveS(); }

    /* ---------------- الجلسات ---------------- */
    public static string? Start(int i, Form f)
    {
        double? v = double.TryParse(f.V, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : null;
        if (f.Type == "timed" && !(v > 0)) return "أدخل المدة بالدقائق";
        if (f.Type == "amount" && !(v > 0)) return "أدخل المبلغ";
        S.Devices[i] = new Dev { On = true, Start = Now(), Mode = f.Mode, Type = f.Type, Mins = f.Type == "timed" ? v!.Value : 0, Amount = f.Type == "amount" ? v!.Value : 0 };
        SaveS(); return null;
    }

    public static void AddDrink(int i, string n, double p)
    {
        var d = S.Devices[i]; var x = d.Drinks.FirstOrDefault(k => k.N == n);
        if (x != null) x.Q++; else d.Drinks.Add(new DrinkQty { N = n, P = p, Q = 1 });
        SaveS();
    }
    public static void SubDrink(int i, string n)
    {
        var d = S.Devices[i]; var x = d.Drinks.FirstOrDefault(k => k.N == n); if (x == null) return;
        if (x.Q <= 1) d.Drinks.Remove(x); else x.Q--;
        SaveS();
    }

    public static void SwMode(int i)
    {
        var d = S.Devices[i]; long n = Now(); long b = d.Seg != 0 ? d.Seg : d.Start; long seg = n - b;
        d.Acc += seg * M.RateOf(d, S.Rates);
        if (d.M0 == 0) d.M0 = d.Mode;
        if (d.Mode == 4) { d.T4 += seg; d.Mode = 2; } else { d.T2 += seg; d.Mode = 4; }
        d.Seg = n; SaveS();
        Toast("تم التحويل إلى " + (d.Mode == 4 ? "٤ لاعبين" : "لاعبين") + " — يُحسب الوقت القادم بالسعر الجديد");
    }

    public static void MoveTo(int i, int j)
    {
        if (S.Devices[j].On) return;
        S.Devices[j] = S.Devices[i]; S.Devices[i] = new Dev();
        if (Ringing == i) StopAlarm();
        SaveS(); Toast($"تم نقل اللاعب من جهاز {i + 1} إلى جهاز {j + 1}");
    }

    public static string? SetStart(int i, long ns)
    {
        var d = S.Devices[i];
        if (!d.On) return "الجهاز غير مشغول";
        if (ns > Now() + 1000) return "وقت البدء لا يمكن أن يكون في المستقبل";
        if (d.Seg != 0 && ns >= d.Seg) return "وقت البدء يجب أن يسبق وقت تحويل النظام";
        long old = d.Start, dl = old - ns;
        if (d.Seg != 0)
        {
            int m0 = d.M0 != 0 ? d.M0 : d.Mode;
            if (m0 == 4) { d.T4 += dl; d.Acc += dl * S.Rates.P4 / 3600000.0; } else { d.T2 += dl; d.Acc += dl * S.Rates.P2 / 3600000.0; }
        }
        d.Start = ns; dismissed.Remove(old); dismissed.Remove(ns);
        return null;
    }

    public static string? ApplyEdit(int i, long ns, string type, double? v, bool fromNow)
    {
        var d = S.Devices[i]; if (!d.On) return null;
        if (Math.Abs(ns - d.Start) >= 60000) { var e = SetStart(i, ns); if (e != null) return e; }
        long n = Now();
        if (type != d.Type || v != null)
        {
            if (type == "open") { d.Type = "open"; d.Mins = 0; d.Amount = 0; }
            else
            {
                if (!(v > 0)) return type == "timed" ? "أدخل المدة بالدقائق" : "أدخل المبلغ";
                if (type == "timed") { d.Mins = fromNow ? (n - d.Start) / 60000.0 + v!.Value : v!.Value; d.Amount = 0; }
                else
                {
                    long b = d.Seg != 0 ? d.Seg : d.Start; double raw = d.Acc + (n - b) * M.RateOf(d, S.Rates);
                    d.Amount = fromNow ? M.Up(raw + v!.Value) : v!.Value; d.Mins = 0;
                }
                d.Type = type;
            }
            d.Cont = false; dismissed.Remove(d.Start);
            if (Ringing == i) StopAlarm();
        }
        SaveS(); return null;
    }

    public static void Finish(int i, long end)
    {
        var e = M.EntryFrom(S.Devices[i], i, end, S.Rates);
        LogPut(e); S.Devices[i] = new Dev();
        if (Ringing == i) StopAlarm();
        SaveS();
    }

    public static void AddDev() { S.Devices.Add(new Dev()); SaveS(); Toast($"تمت إضافة جهاز {S.Devices.Count}"); }
    public static void DelDev()
    {
        int n = S.Devices.Count - 1;
        if (n < 1) { Toast("يجب بقاء جهاز واحد على الأقل"); return; }
        if (S.Devices[n].On) { Toast($"الجهاز {n + 1} مشغول، أنهِ جلسته أولاً"); return; }
        S.Devices.RemoveAt(n); SaveS();
    }

    /* ---------------- السجل ---------------- */
    public static void LogPut(LogEntry e)
    {
        if (!Logs.Any(x => x.Id == e.Id)) Logs.Add(e);
        SaveAll();
        if (Cloud) RtWrite("PUT", "logs/" + e.Id, e.ToJson().ToJsonString());
    }
    public static void DelLog(string id)
    {
        Logs.RemoveAll(x => x.Id == id); SaveAll();
        if (Cloud) RtWrite("DELETE", "logs/" + id, null);
        Changed?.Invoke(); Toast("تم الحذف");
    }
    public static bool Sale(Dictionary<int, int> cart)
    {
        var dr = cart.Where(k => k.Value > 0 && k.Key < S.Drinks.Count).Select(k => new DrinkQty { N = S.Drinks[k.Key].N, P = S.Drinks[k.Key].P, Q = k.Value }).ToList();
        if (dr.Count == 0) { Toast("اختر مشروباً أولاً"); return false; }
        long n = Now(); double t = dr.Sum(x => x.P * x.Q);
        LogPut(new LogEntry { Id = $"s{n}_{Random.Shared.Next(1000, 9999)}", Dev = 0, Mode = 2, M0 = 2, Type = "sale", Start = n, End = n, Drinks = dr, DrinkTotal = t, Total = t, Day = M.DKey(n) });
        Changed?.Invoke(); Toast("تم البيع: " + M.Fmt(t)); return true;
    }
    public static double RatePerMs(int mode) => (mode == 4 ? S.Rates.P4 : S.Rates.P2) / 3600000.0;

    /// <summary>إضافة جلسة سابقة. المبلغ والمشروبات محسوبة مسبقاً من الواجهة.</summary>
    public static LogEntry AddOld(int dev, int mode, long st, long en, double cost, List<DrinkQty> drinks)
    {
        double len = en - st, dt = drinks.Sum(k => k.P * k.Q);
        var x = new LogEntry
        {
            Id = "m" + Now() + "_" + Random.Shared.Next(100, 999), Dev = dev, Mode = mode, M0 = mode, Type = "open", Start = st, End = en,
            T2 = mode == 4 ? 0 : len, T4 = mode == 4 ? len : 0, C2 = mode == 4 ? 0 : cost, C4 = mode == 4 ? cost : 0,
            TimeCost = cost, Drinks = drinks, DrinkTotal = dt, Total = cost + dt, Day = M.DKey(st)
        };
        LogPut(x); return x;
    }

    public static void SaveRates(double p2, double p4) { S.Rates = new Rates { P2 = p2, P4 = p4 }; SaveS(); }
    public static void AddDrinkDef(string n, double p) { S.Drinks.Add(new DrinkDef { N = n, P = p }); SaveS(); }
    public static void DelDrinkDef(int j) { S.Drinks.RemoveAt(j); SaveS(); }

    /* ---------------- القفل ---------------- */
    public static void SetPin(string pin)
    {
        bool had = LockHash != null;
        LockHash = M.PinHash(pin); SetOK = true; SaveAll();
        SendAlert("change", had ? "🔐 تم تغيير الرمز السري" : "🔒 تم تفعيل قفل الإعدادات");
    }
    public static void PinOff() { LockHash = null; SetOK = false; SaveAll(); SendAlert("off", "🔓 تم إيقاف قفل الإعدادات"); }
    public static bool CheckPin(string p) => LockHash == M.PinHash(p);

    /// <summary>يتحقق من الرمز؛ عند الخطأ يُرسل إنذاراً للجهاز الآخر. where: settings | delete</summary>
    public static bool TryPin(string p, string where)
    {
        if (CheckPin(p)) return true;
        SendAlert("wrong", where == "delete" ? "⚠️ رمز خاطئ عند محاولة حذف عملية من السجل" : "⚠️ رمز خاطئ عند محاولة فتح الإعدادات");
        return false;
    }

    /* ---------------- تنبيهات أمنية بين الجهازين ---------------- */
    public static void SendAlert(string kind, string msg)
    {
        if (Rt == null) return;
        var id = $"a{Now()}_{Random.Shared.Next(1000, 9999)}";
        var o = new JsonObject { ["t"] = Now(), ["dev"] = devId, ["name"] = DevName, ["kind"] = kind, ["msg"] = msg };
        RtWrite("PUT", "alerts/" + id, o.ToJsonString());
    }

    static void OnAlerts(JsonNode? v)
    {
        var list = new List<(long T, string Name, string Msg)>();
        if (v is JsonObject o)
            foreach (var kv in o)
                if (kv.Value is JsonObject a && M.Str(a["dev"]) != devId)
                    list.Add(((long)M.D(a["t"]), M.Str(a["name"], "جهاز آخر"), M.Str(a["msg"])));
        UI(() =>
        {
            if (alertSeen == 0) { alertSeen = Now(); SaveAll(); return; }   // أول اتصال: تجاهل القديم
            bool any = false;
            foreach (var x in list.Where(k => k.T > alertSeen).OrderBy(k => k.T))
            {
                alertSeen = Math.Max(alertSeen, x.T); any = true;
                AlertFn($"{x.Msg}\n(من: {x.Name})");
            }
            if (any) SaveAll();
        });
    }

    /* ---------------- حفظ ملف Excel ---------------- */
    public static void SaveXlsx(string name, byte[] data)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = name, Filter = "Excel (*.xlsx)|*.xlsx", DefaultExt = ".xlsx", AddExtension = true };
        if (dlg.ShowDialog() != true) return;
        try { File.WriteAllBytes(dlg.FileName, data); Toast("تم حفظ ملف Excel"); }
        catch (Exception e) { Toast("تعذر حفظ الملف: " + e.Message); }
    }

    /* ---------------- المزامنة: Firebase Realtime Database (REST + SSE) ---------------- */
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static string UrlOf(RtCfg c, string p) => $"{c.Url}/{p}.json?auth={Uri.EscapeDataString(c.Sec)}";

    static async Task<string> Req(RtCfg c, HttpMethod m, string p, string? body)
    {
        using var r = new HttpRequestMessage(m, UrlOf(c, p));
        if (body != null) r.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(r);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException("http", null, resp.StatusCode);
        return await resp.Content.ReadAsStringAsync();
    }

    static void Queue(string m, string p, string? v)
    {
        if (!p.StartsWith("logs/")) return;
        lock (saveLock) { pend = pend.Where(x => x.P != p).Append(new Pend(m, p, v)).ToList(); }
        SaveAll();
    }

    public static void RtWrite(string m, string p, string? v)
    {
        var c = Rt; if (c == null) return;
        _ = Task.Run(async () =>
        {
            try { await Req(c, new HttpMethod(m), p, v); }
            catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { Toast("كلمة السر غير صحيحة"); }
            catch { Queue(m, p, v); }
        });
    }

    static void Flush()
    {
        List<Pend> q; lock (saveLock) { q = pend; pend = new(); }
        if (q.Count == 0) return;
        SaveAll();
        foreach (var x in q) RtWrite(x.M, x.P, x.V);
    }

    class Tree
    {
        public JsonNode? Root;
        public void Apply(string path, JsonNode? d)
        {
            var k = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (k.Length == 0) { Root = d?.DeepClone(); return; }
            if (Root is not JsonObject) Root = new JsonObject();
            var o = (JsonObject)Root;
            for (int i = 0; i < k.Length - 1; i++)
            {
                if (o[k[i]] is JsonObject c) o = c; else { var n = new JsonObject(); o[k[i]] = n; o = n; }
            }
            if (d == null) o.Remove(k[^1]); else o[k[^1]] = d.DeepClone();
        }
    }

    static CancellationTokenSource? cts;

    static void Listen(string path, Action<JsonNode?> onVal, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            var tree = new Tree();
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var cfg = Rt; if (cfg == null) break;
                    using var cl = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                    using var req = new HttpRequestMessage(HttpMethod.Get, UrlOf(cfg, path));
                    req.Headers.Accept.ParseAdd("text/event-stream");
                    using var resp = await cl.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { Toast("كلمة السر غير صحيحة أو تم إلغاؤها"); break; }
                    resp.EnsureSuccessStatusCode();
                    UI(() => { Live = true; Flush(); Soft?.Invoke(); });
                    using var st = await resp.Content.ReadAsStreamAsync(ct);
                    using var rd = new StreamReader(st);
                    string ev = ""; var data = new StringBuilder();
                    while (true)
                    {
                        using var lc = CancellationTokenSource.CreateLinkedTokenSource(ct); lc.CancelAfter(90000);
                        var line = await rd.ReadLineAsync(lc.Token);
                        if (line == null) break;
                        if (line.StartsWith("event:")) ev = line[6..].Trim();
                        else if (line.StartsWith("data:")) { if (data.Length > 0) data.Append('\n'); data.Append(line[5..].Trim()); }
                        else if (line.Length == 0)
                        {
                            if (ev == "put" || ev == "patch")
                            {
                                var m = JsonNode.Parse(data.ToString())!.AsObject();
                                string p = M.Str(m["path"], "/");
                                if (ev == "put") tree.Apply(p, m["data"]);
                                else if (m["data"] is JsonObject dd) foreach (var kv in dd.ToList()) tree.Apply(p.TrimEnd('/') + "/" + kv.Key, kv.Value);
                                onVal(tree.Root);
                            }
                            else if (ev == "cancel" || ev == "auth_revoked") { Toast("كلمة السر غير صحيحة أو تم إلغاؤها"); }
                            ev = ""; data.Clear();
                        }
                    }
                }
                catch { }
                UI(() => { Live = false; Soft?.Invoke(); });
                try { await Task.Delay(3000, ct); } catch { break; }
            }
        });
    }

    static void OnState(JsonNode? v)
    {
        if (v is JsonObject r && M.Str(r["json"]) is { Length: > 0 } js)
        {
            AppState n;
            try { n = M.StateFrom(JsonNode.Parse(js)!.AsObject()); } catch { return; }
            UI(() =>
            {
                if (n.Ts >= S.Ts) { S = n; SaveAll(); Changed?.Invoke(); }
                else RtWrite("PUT", "lounge/state", new JsonObject { ["ts"] = S.Ts, ["json"] = S.ToJson().ToJsonString() }.ToJsonString());
            });
        }
        else UI(SaveS);
    }

    static void OnLogs(JsonNode? v)
    {
        var a = new List<LogEntry>();
        if (v is JsonObject o)
            foreach (var kv in o)
                if (kv.Value is JsonObject lo) { try { a.Add(M.LogFrom(lo)); } catch { } }
        List<Pend> q; lock (saveLock) { q = pend.ToList(); }
        foreach (var x in q)
        {
            var id = x.P["logs/".Length..];
            if (x.M == "DELETE") a.RemoveAll(l => l.Id == id);
            else if (!a.Any(l => l.Id == id) && x.V != null) { try { a.Add(M.LogFrom(JsonNode.Parse(x.V)!.AsObject())); } catch { } }
        }
        UI(() => { Logs = a; SaveAll(); Changed?.Invoke(); });
    }

    public static void StartCloud()
    {
        SyncClock();
        cts?.Cancel(); cts = new CancellationTokenSource();
        Listen("lounge/state", OnState, cts.Token);
        Listen("logs", OnLogs, cts.Token);
        Listen("alerts", OnAlerts, cts.Token);
    }

    public static void SyncClock()
    {
        var c = Rt; if (c == null) return;
        _ = Task.Run(async () =>
        {
            long bestRtt = long.MaxValue; double bestOff = 0; bool ok = false;
            for (int k = 0; k < 3; k++)
            {
                long t0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                try
                {
                    var body = await Req(c, HttpMethod.Put, "clock/" + devId, "{\".sv\":\"timestamp\"}");
                    if (!double.TryParse(body.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sv))
                        sv = double.Parse((await Req(c, HttpMethod.Get, "clock/" + devId, null)).Trim(), System.Globalization.CultureInfo.InvariantCulture);
                    long t1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); long rtt = t1 - t0;
                    if (rtt < bestRtt) { bestRtt = rtt; bestOff = sv - (t0 + t1) / 2.0; ok = true; }
                }
                catch { }
                await Task.Delay(250);
            }
            if (ok) { Offset = (long)Math.Round(bestOff); SaveAll(); }
        });
    }

    public static string NormUrl(string u0)
    {
        var u = System.Text.RegularExpressions.Regex.Replace(u0.Trim(), @"\.json.*$", "").TrimEnd('/');
        if (!System.Text.RegularExpressions.Regex.IsMatch(u, "^https?://", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) u = "https://" + u;
        return System.Text.RegularExpressions.Regex.Replace(u, "^http://", "https://", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public static void Link(string url, string sec, Action<string?> cb)
    {
        var c = new RtCfg(NormUrl(url), sec.Trim());
        _ = Task.Run(async () =>
        {
            string? err = null;
            try { await Req(c, HttpMethod.Get, "lounge/state/ts", null); }
            catch (HttpRequestException e) { err = e.StatusCode switch { HttpStatusCode.Unauthorized => "كلمة السر غير صحيحة", HttpStatusCode.NotFound => "رابط قاعدة البيانات غير صحيح", null => "تعذر الاتصال — تأكد من الرابط ومن الإنترنت", _ => "خطأ من الخادم" }; }
            catch { err = "تعذر الاتصال — تأكد من الرابط ومن الإنترنت"; }
            UI(() =>
            {
                if (err == null) { Rt = c; Cloud = true; SaveAll(); StartCloud(); }
                cb(err);
            });
        });
    }

    public static void Unlink()
    {
        cts?.Cancel(); Rt = null; Cloud = false; Live = false; SaveAll(); Soft?.Invoke();
    }
}
