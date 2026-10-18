using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace BecquerelMonitor
{
    public class AtomSpectraRestDataReadyArgs : AtomSpectraTcpDataReadyArgs
    {
        public double LiveSeconds { get; private set; }
        public long Total { get; private set; }
        // "dead" of the board is a 0/1 flag "the analyzer is silent" (spectrum_meta_fmt.h:111), NOT a dead time.
        public bool SpectrometerSilent { get; private set; }
        public long HistOk { get; private set; }            // "hist_ok": monotonic until a reboot
        public double[] Calibration { get; private set; }   // "calib" (up to 5 slots) when "calib_set", else null
        // F5 (plan K0-K4): fields of the v2 answer (the request carried ?channels=N)
        public int Channels { get; set; } = -1;              // top-level "channels"; -1: absent (old firmware / v1 answer)
        public int CalibChannels { get; set; } = AtomSpectraRestSpectrum.Channels;   // "calib_channels": the scale of "calib"
        public AtomSpectraRestEpoch Epoch { get; set; }      // boot_id / dev_resets / spec_resets; null: none of them

        public AtomSpectraRestDataReadyArgs(
            int[] histogram,
            int cpuLoad,
            int elapsedTime,
            int invalidPulses,
            int cps,
            double liveSeconds,
            long total,
            bool spectrometerSilent,
            long histOk,
            double[] calibration)
            : base(histogram, cpuLoad, elapsedTime, invalidPulses, cps)
        {
            LiveSeconds = liveSeconds;
            Total = total;
            SpectrometerSilent = spectrometerSilent;
            HistOk = histOk;
            Calibration = calibration;
        }
    }

    public static class AtomSpectraRestSpectrum
    {
        public const int Channels = 8192;   // the board's full scale; the only length of an answer without "channels"

        // F5 (plan K1): the N the board folds to; a profile with another number of channels sends no parameter
        public static readonly int[] RequestableChannels = { 8192, 4096, 2048, 1024 };

        public static int RequestChannels(int profileChannels)
        {
            return Array.IndexOf(RequestableChannels, profileChannels) >= 0 ? profileChannels : 0;
        }

        // double.IsFinite does not exist in .NET Framework 4.8
        public static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }

        private static long? Whole(Dictionary<string, object> o, string key)
        {
            double v = AtomSpectraJson.GetNumber(o, key, double.NaN);
            return Finite(v) ? (long?)(long)v : null;
        }

        public static AtomSpectraRestBoardSample ToSample(AtomSpectraRestDataReadyArgs d, long atMs)
        {
            return new AtomSpectraRestBoardSample { HasData = true, Time = d.ElapsedTime, Total = d.Total, HistOk = d.HistOk, AtMs = atMs, Bins = d.Histogram, Epoch = d.Epoch };
        }

        // /api/spectrum.json is the primary source: one snapshot carries counts, time, live time and rejected pulses together;
        // the binary /api/spectrum (32 KB) has no time, so the pair "spectrum - time" could not be built from it alone.
        public static AtomSpectraRestDataReadyArgs Parse(string json, out string error)
        {
            return Parse(json, 0, out error);
        }

        // F5 (plan K1): "channels", when present, must equal the bins; then the length must be the N asked for (8192 for a
        // request without the parameter). Without "channels" only the old 8192 answer is taken (the client folds it).
        public static string LengthError(int bins, int declared, int requested)
        {
            if (declared < 0) return bins == Channels ? null : "bins count " + bins + " without \"channels\" (only " + Channels + ")";
            if (declared != bins) return "\"channels\" " + declared + " != bins count " + bins;
            int wanted = requested > 0 ? requested : Channels;
            return bins == wanted ? null : "\"channels\" " + bins + " != requested " + wanted;
        }

        // requested: the N of ?channels=N (0: the request had no parameter)
        public static AtomSpectraRestDataReadyArgs Parse(string json, int requested, out string error)
        {
            error = null;
            try
            {
                object root = AtomSpectraJson.Parse(json);
                if (!(root is Dictionary<string, object>))
                {
                    error = "not an object";
                    return null;
                }

                Dictionary<string, object> o = (Dictionary<string, object>)root;

                List<object> bins = AtomSpectraJson.GetArray(o, "bins");
                if (bins == null)
                {
                    error = "no bins";
                    return null;
                }

                int declared = o.ContainsKey("channels") ? (int)AtomSpectraJson.GetNumber(o, "channels", -1) : -1;
                string lengthError = LengthError(bins.Count, declared, requested);
                if (lengthError != null)
                {
                    error = lengthError;
                    return null;
                }

                int n = bins.Count;
                int[] histogram = new int[n];
                long sum = 0;
                for (int i = 0; i < n; i++)
                {
                    object item = bins[i];
                    if (!(item is double))
                    {
                        error = "bin " + i + " is not a number";
                        return null;
                    }

                    double val = (double)item;
                    if (!AtomSpectraRestSpectrum.Finite(val) || val < 0 || val > int.MaxValue)
                    {
                        error = "bin " + i + " out of range";
                        return null;
                    }

                    int count = (int)val;
                    histogram[i] = count;
                    sum += count;
                }

                if (!o.ContainsKey("time"))
                {
                    error = "missing time";
                    return null;
                }

                double timeVal = AtomSpectraJson.GetNumber(o, "time", 0);
                int elapsed = (int)timeVal;

                double cpuVal = AtomSpectraJson.GetNumber(o, "cpu", 0);
                int cpu = (int)cpuVal;

                double cpsVal = AtomSpectraJson.GetNumber(o, "cps", 0);
                int cps = (int)cpsVal;

                double lostVal = AtomSpectraJson.GetNumber(o, "lost", 0);
                int invalid = (int)lostVal;

                double liveVal = double.NaN;
                if (o.ContainsKey("live"))
                {
                    double lv = AtomSpectraJson.GetNumber(o, "live", double.NaN);
                    if (AtomSpectraRestSpectrum.Finite(lv))
                    {
                        liveVal = lv;
                    }
                }

                long total = sum;
                if (o.ContainsKey("total"))
                {
                    double tv = AtomSpectraJson.GetNumber(o, "total", sum);
                    if (AtomSpectraRestSpectrum.Finite(tv))
                    {
                        total = (long)tv;
                    }
                }

                bool silent = AtomSpectraJson.GetNumber(o, "dead", 0) != 0;
                double hv = AtomSpectraJson.GetNumber(o, "hist_ok", 0);
                long histOk = AtomSpectraRestSpectrum.Finite(hv) && hv > 0 ? (long)hv : 0;
                double[] calib = null;
                List<object> cal = AtomSpectraJson.GetArray(o, "calib");
                if (cal != null && AtomSpectraJson.GetBool(o, "calib_set", true))
                {
                    calib = new double[Math.Min(5, cal.Count)];
                    for (int i = 0; i < calib.Length; i++) calib[i] = cal[i] is double ? (double)cal[i] : double.NaN;
                }

                AtomSpectraRestEpoch epoch = new AtomSpectraRestEpoch { BootId = Whole(o, "boot_id"), DevResets = Whole(o, "dev_resets"), SpecResets = Whole(o, "spec_resets") };
                long? calibChannels = Whole(o, "calib_channels");

                return new AtomSpectraRestDataReadyArgs(
                    histogram,
                    cpu,
                    elapsed,
                    invalid,
                    cps,
                    liveVal,
                    total,
                    silent,
                    histOk,
                    calib)
                {
                    Channels = declared,
                    CalibChannels = calibChannels.HasValue && calibChannels.Value > 0 && calibChannels.Value <= int.MaxValue ? (int)calibChannels.Value : Channels,
                    Epoch = epoch.Any ? epoch : null
                };
            }
            catch (Exception ex)
            {
                error = "parse exception: " + ex.Message;
                return null;
            }
        }
    }

    public sealed class AtomSpectraRestSampleArgs : EventArgs
    {
        public AtomSpectraRestBoardSample Sample;
        public AtomSpectraRestDataReadyArgs Data;    // null for HTTP 404 "No spectrum data yet"
    }

    public sealed class AtomSpectraRestAcquisition : IDisposable
    {
        public const int LinkLostAfterFailures = 3;
        public const int MinDeadlineMs = 2000;
        public const int StatsEveryMs = 5000;

        private readonly AtomSpectraRestClient _client;
        private readonly int _periodMs;
        private readonly int _requestChannels;              // F5 (plan K1): N of ?channels=N, 0 = no parameter
        private readonly AtomSpectraRestPeriod _period;     // F5 (B6): the effective period
        private long _lastAnsweredMs;                       // F5: clock time of the last answer (0 = the start of the clock)
        private readonly ManualResetEvent _stopEvent = new ManualResetEvent(false);
        private Thread _thread;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private volatile bool _alive;
        private volatile bool _linkLost;
        private int _consecutiveFailures;
        private int _attempts;
        private int _successes;
        private int _failures;
        private int _noData;
        private int _maxInFlight;
        private int _inFlight;
        private int _lastWireBytes;
        private string _lastEncoding;
        private string _lastError;
        private AtomSpectraRestDataReadyArgs _last;
        private volatile AtomSpectraRestBoardSample _lastSample;

        // Every answered poll: a snapshot, or HTTP 404 "No spectrum data yet" (Data == null). The session decides what it means.
        public event EventHandler<AtomSpectraRestSampleArgs> Sampled;
        public event EventHandler LinkLost;
        public event EventHandler LinkRestored;

        public AtomSpectraRestAcquisition(AtomSpectraRestClient client, int periodSeconds)
            : this(client, periodSeconds, 0)
        {
        }

        public AtomSpectraRestAcquisition(AtomSpectraRestClient client, int periodSeconds, int requestChannels)
        {
            if (client == null) throw new ArgumentNullException("client");
            _client = client;
            _requestChannels = AtomSpectraRestSpectrum.RequestChannels(requestChannels);

            int clamped = periodSeconds;
            if (clamped < 1) clamped = 1;
            if (clamped > 60) clamped = 60;
            _periodMs = clamped * 1000;
            _period = new AtomSpectraRestPeriod(_periodMs);
        }

        public int DeadlineMs
        {
            get
            {
                // plan 3 / K5: max(2 s, period / 2) of the effective period (B6 lengthens both); at 1 s the poll may
                // outlive the period, Run then skips ticks (no queue)
                return Math.Max(MinDeadlineMs, _period.EffectiveMs / 2);
            }
        }

        public int EffectivePeriodMs { get { return _period.EffectiveMs; } }
        public string SpectrumPath { get { return "/api/spectrum.json" + (_requestChannels > 0 ? "?channels=" + _requestChannels : ""); } }

        public bool IsLinkLost
        {
            get { return _linkLost; }
        }

        public bool IsRunning
        {
            get { return _thread != null && _thread.IsAlive; }
        }

        public int PeriodMs
        {
            get { return _periodMs; }
        }

        // Counters for the log and the tests
        public int Attempts { get { return Volatile.Read(ref _attempts); } }
        public int Successes { get { return Volatile.Read(ref _successes); } }
        public int Failures { get { return Volatile.Read(ref _failures); } }
        public int MaxInFlight { get { return Volatile.Read(ref _maxInFlight); } }
        public string LastError { get { return _lastError; } }
        public int NoDataAnswers { get { return Volatile.Read(ref _noData); } }
        public AtomSpectraRestDataReadyArgs Last { get { return _last; } }
        public AtomSpectraRestBoardSample LastSample { get { return _lastSample; } }
        public long NowMs { get { return _clock.ElapsedMilliseconds; } }
        // Age of the last answered poll, s (-1: none yet): "data is N s old" in the log window (plan M5).
        public double DataAgeSeconds { get { AtomSpectraRestBoardSample l = _lastSample; return l == null ? -1 : (NowMs - l.AtMs) / 1000.0; } }

        public void Start()
        {
            if (_thread != null) return;

            _alive = true;
            _stopEvent.Reset();

            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "AtomSpectraRest"
            };

            AtomSpectraTcpLog.Info("REST", "polling " + _client.BaseUrl + "/api/spectrum.json every " + (_periodMs / 1000) + " s, deadline " + DeadlineMs + " ms");

            _thread.Start();
        }

        // Ask the poll thread to end after the request in flight, without waiting for it (pass B P-1).
        public void Signal()
        {
            _alive = false;
            _stopEvent.Set();
        }

        public void Dispose()
        {
            if (!_alive && _thread == null) return;

            _alive = false;
            _stopEvent.Set();

            if (_thread != null && _thread != Thread.CurrentThread)
            {
                _thread.Join(DeadlineMs + 1000);
                if (_thread.IsAlive)
                {
                    AtomSpectraTcpLog.Warn("REST", "poll thread did not stop in time");
                }
                _thread = null;
            }
        }

        public bool PollOnce()
        {
            int now = Interlocked.Increment(ref _inFlight);
            // update MaxInFlight with a compare loop
            while (true)
            {
                int current = Volatile.Read(ref _maxInFlight);
                if (now <= current) break;
                if (Interlocked.CompareExchange(ref _maxInFlight, now, current) == current) break;
            }

            try
            {
                Interlocked.Increment(ref _attempts);

                AtomSpectraRestResponse r = _client.Get(SpectrumPath, DeadlineMs);
                _lastWireBytes = r.WireBytes;
                _lastEncoding = r.ContentEncoding;

                if (r.Status == 404)
                {
                    Interlocked.Increment(ref _noData);
                    MarkAnswered();
                    AtomSpectraRestBoardSample none = AtomSpectraRestBoardSample.NoData(NowMs);
                    _lastSample = none;
                    AtomSpectraTcpLog.Info("REST", "404 No spectrum data yet" + (r.SpectrometerDead ? " (X-Spectrometer-Dead)" : "") + ": the board has no spectrum, the link is fine");
                    RaiseSampled(none, null);
                    return true;
                }

                string error = r.Ok ? null : (r.Error ?? "HTTP " + r.Status);
                AtomSpectraRestDataReadyArgs data = null;

                if (error == null)
                {
                    data = AtomSpectraRestSpectrum.Parse(r.Body, _requestChannels, out error);
                }

                if (error != null)
                {
                    Interlocked.Increment(ref _failures);
                    int cf = Interlocked.Increment(ref _consecutiveFailures);
                    _lastError = error;
                    int periodInForce = _period.EffectiveMs;   // the period this poll was scheduled with, before the doubling below
                    // F5 (B6): missed deadline, transport error or an answer that did not parse = a skipped poll: no Trip
                    PeriodAfter(false);

                    AtomSpectraTcpLog.Warn("REST", "poll failed (" + cf + " in a row): " + error);

                    // F5: also by the time without an answer: 3 periods, the larger of the configured one and the one in force
                    // (finding 2: with the period doubled by skipped polls, 3 configured periods are only 1-2 polls)
                    long silentMs = NowMs - Interlocked.Read(ref _lastAnsweredMs);
                    bool silent = silentMs >= 3L * Math.Max(_periodMs, periodInForce);
                    if ((cf == LinkLostAfterFailures || silent) && !_linkLost)
                    {
                        _linkLost = true;
                        AtomSpectraTcpLog.Error("REST", "link lost after " + cf + " failed polls, " + silentMs + " ms without an answer");
                        RaiseLinkLost();
                    }

                    return false;
                }

                Interlocked.Increment(ref _successes);
                _last = data;
                MarkAnswered();
                PeriodAfter(true);
                AtomSpectraRestBoardSample sample = AtomSpectraRestSpectrum.ToSample(data, NowMs);
                _lastSample = sample;
                RaiseSampled(sample, data);
                return true;
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        // F5 (B6): full = a snapshot taken; a 404 "No spectrum data yet" is neither (the period stays, plan K6)
        private void PeriodAfter(bool full)
        {
            int before = _period.EffectiveMs;
            if (full ? _period.Full() : _period.Skip())
            {
                AtomSpectraTcpLog.StaleAfterSeconds = 2 * _period.EffectiveMs / 1000 + 5;   // the freshness mark of the log window follows the period in force
                AtomSpectraTcpLog.Info("REST", "poll period " + (before / 1000.0).ToString(CultureInfo.InvariantCulture) + " s -> " +
                    (_period.EffectiveMs / 1000.0).ToString(CultureInfo.InvariantCulture) + " s, deadline " + DeadlineMs + " ms" +
                    (full ? " (" + AtomSpectraRestPeriod.FullToHalve + " full polls in a row)" : " (skipped poll: " + _lastError + ")"));
            }
        }

        private void MarkAnswered()
        {
            Interlocked.Exchange(ref _lastAnsweredMs, NowMs);   // a snapshot or a 404: the board answers
            if (_linkLost)
            {
                _linkLost = false;
                AtomSpectraTcpLog.Info("REST", "link restored");
                RaiseLinkRestored();
            }
            Interlocked.Exchange(ref _consecutiveFailures, 0);
        }

        private void Run()
        {
            long next = _clock.ElapsedMilliseconds;
            long nextStats = next + StatsEveryMs;

            while (_alive)
            {
                long wait = next - _clock.ElapsedMilliseconds;
                if (wait > 0 && _stopEvent.WaitOne((int)wait)) break;
                if (!_alive) break;

                PollOnce();

                next += _period.EffectiveMs;

                long nowMs = _clock.ElapsedMilliseconds;
                if (next <= nowMs) next = nowMs + _period.EffectiveMs;

                if (nowMs >= nextStats)
                {
                    LogStats();
                    nextStats = nowMs + StatsEveryMs;
                }
            }
        }

        private void LogStats()
        {
            int attempts = Volatile.Read(ref _attempts);
            int successes = Volatile.Read(ref _successes);
            int failures = Volatile.Read(ref _failures);
            int wireBytes = Volatile.Read(ref _lastWireBytes);
            string encoding = _lastEncoding ?? "uncompressed";
            if (encoding == null || encoding.Length == 0) encoding = "uncompressed";

            long total = 0;
            double time = 0;
            double live = 0;
            double dead = 0;
            int cps = 0;

            if (_last != null)
            {
                total = _last.Total;
                time = _last.ElapsedTime;
                live = _last.LiveSeconds;
                dead = _last.SpectrometerSilent ? 1 : 0;
                cps = _last.Cps;
            }

            string msg = "REST stats: period " + (_period.EffectiveMs / 1000.0).ToString(CultureInfo.InvariantCulture) + " s (configured " + (_periodMs / 1000) + " s), polls " + attempts +
                         " (ok " + successes + ", failed " + failures + "), last response " + wireBytes +
                         " B (" + encoding + "), last total " + total +
                         ", time " + time.ToString(CultureInfo.InvariantCulture) + " s, live " +
                         live.ToString(CultureInfo.InvariantCulture) + " s, " +
                         (dead > 0 ? "analyzer silent (dead=1)" : "dead=0") + ", cps " + cps + ", 404 answers " + Volatile.Read(ref _noData);

            AtomSpectraTcpLog.Info("REST", msg);
        }

        private void RaiseSampled(AtomSpectraRestBoardSample sample, AtomSpectraRestDataReadyArgs data)
        {
            EventHandler<AtomSpectraRestSampleArgs> handler = Sampled;
            if (handler != null)
            {
                try
                {
                    handler(this, new AtomSpectraRestSampleArgs { Sample = sample, Data = data });
                }
                catch (Exception ex)
                {
                    AtomSpectraTcpLog.Warn("REST", "Sampled handler exception: " + ex.Message);
                }
            }
        }

        private void RaiseLinkLost()
        {
            EventHandler handler = LinkLost;
            if (handler != null)
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    AtomSpectraTcpLog.Warn("REST", "LinkLost handler exception: " + ex.Message);
                }
            }
        }

        private void RaiseLinkRestored()
        {
            EventHandler handler = LinkRestored;
            if (handler != null)
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    AtomSpectraTcpLog.Warn("REST", "LinkRestored handler exception: " + ex.Message);
                }
            }
        }
    }
}
