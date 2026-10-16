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
        public const int Channels = 8192;

        // double.IsFinite does not exist in .NET Framework 4.8
        public static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }

        public static AtomSpectraRestBoardSample ToSample(AtomSpectraRestDataReadyArgs d, long atMs)
        {
            return new AtomSpectraRestBoardSample { HasData = true, Time = d.ElapsedTime, Total = d.Total, HistOk = d.HistOk, AtMs = atMs, Bins = d.Histogram };
        }

        // /api/spectrum.json is the primary source: one snapshot carries counts, time, live time and rejected pulses together;
        // the binary /api/spectrum (32 KB) has no time, so the pair "spectrum - time" could not be built from it alone.
        public static AtomSpectraRestDataReadyArgs Parse(string json, out string error)
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

                if (bins.Count != Channels)
                {
                    error = "bins count != " + Channels;
                    return null;
                }

                int[] histogram = new int[Channels];
                long sum = 0;
                for (int i = 0; i < Channels; i++)
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
                    calib);
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
        {
            if (client == null) throw new ArgumentNullException("client");
            _client = client;

            int clamped = periodSeconds;
            if (clamped < 1) clamped = 1;
            if (clamped > 60) clamped = 60;
            _periodMs = clamped * 1000;
        }

        public int DeadlineMs
        {
            get
            {
                // plan 3: max(2 s, period / 2); at 1 s the poll may outlive the period, Run then skips ticks (no queue)
                return Math.Max(MinDeadlineMs, _periodMs / 2);
            }
        }

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

                AtomSpectraRestResponse r = _client.Get("/api/spectrum.json", DeadlineMs);
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
                    data = AtomSpectraRestSpectrum.Parse(r.Body, out error);
                }

                if (error != null)
                {
                    Interlocked.Increment(ref _failures);
                    int cf = Interlocked.Increment(ref _consecutiveFailures);
                    _lastError = error;

                    AtomSpectraTcpLog.Warn("REST", "poll failed (" + cf + " in a row): " + error);

                    if (cf == LinkLostAfterFailures && !_linkLost)
                    {
                        _linkLost = true;
                        AtomSpectraTcpLog.Error("REST", "link lost after " + cf + " failed polls");
                        RaiseLinkLost();
                    }

                    return false;
                }

                Interlocked.Increment(ref _successes);
                _last = data;
                MarkAnswered();
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

        private void MarkAnswered()
        {
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

                next += _periodMs;

                long nowMs = _clock.ElapsedMilliseconds;
                if (next <= nowMs) next = nowMs + _periodMs;

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

            string msg = "REST stats: period " + (_periodMs / 1000) + " s, polls " + attempts +
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
