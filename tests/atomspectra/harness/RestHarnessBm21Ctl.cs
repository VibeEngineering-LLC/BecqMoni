// #BM-21: scenarios of the controller (TCP and REST) for the firmware warning; real build only (the controller is not in the source build).
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static int cmdPort;
        static bool bm21Na;

        static int Bm21ClosedPort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        static int Bm21StatusRequests()
        {
            return Requests().Count(r => Str(r, "path") == "/api/status");
        }

        static bool Bm21Begin(bool rest, int tcpPort, out string refusal)
        {
            bm21Na = false;
            refusal = null;

            if (CtlT == null)
            {
                Console.WriteLine("n/a: the controller is not in this build");
                bm21Na = true;
                return true;
            }

            AtomSpectraTcpDeviceConfig cfg = new AtomSpectraTcpDeviceConfig();
            if (rest)
            {
                cfg.Transport = AtomSpectraTransport.Rest;
                cfg.RestUrl = "http://127.0.0.1:" + port + "/";
                cfg.Host = "";
                cfg.RestPort = port;
            }
            else
            {
                cfg.Transport = AtomSpectraTransport.TcpBridge;
                cfg.Host = "127.0.0.1";
                cfg.TcpPort = tcpPort;
                cfg.RestPort = port;
            }

            object info = Activator.CreateInstance(CfgT);
            CfgT.GetProperty("NumberOfChannels").SetValue(info, 8192);
            CfgT.GetProperty("InputDeviceConfig").SetValue(info, cfg);

            object rd = Activator.CreateInstance(RdT);
            RdT.GetProperty("DeviceConfig").SetValue(rd, info);

            object ctl = Activator.CreateInstance(CtlT);
            MethodInfo m = CtlT.GetMethod("BeginSession", NonPub);

            if (m == null)
            {
                Console.WriteLine("n/a: the controller is not in this build");
                bm21Na = true;
                return true;
            }

            try
            {
                object result = m.Invoke(ctl, new object[] { rd, true });
                refusal = null;
                return (bool)result;
            }
            catch (TargetInvocationException e)
            {
                refusal = e.InnerException.Message;
                Console.WriteLine("refusal: " + refusal);
                return false;
            }
        }

        static bool RunBm21Ctl(string scenario)
        {
            switch (scenario)
            {
                case "bm21-ctl-connfail":
                    {
                        string refusal;
                        Bm21Begin(false, Bm21ClosedPort(), out refusal);
                        if (!bm21Na)
                        {
                            Check(refusal != null, "refusal is not null");
                            if (refusal != null)
                            {
                                Check(refusal.Contains(AtomSpectraFirmware.WarningText("1.2.38")), "the refusal carries the firmware hint");
                            }
                            Check(Bm21StatusRequests() == 1, "one status request");
                        }
                        return true;
                    }
                case "bm21-ctl-noanswer":
                case "bm21-ctl-linkclosed":
                    {
                        string refusal;
                        Bm21Begin(false, cmdPort, out refusal);
                        if (!bm21Na)
                        {
                            Check(refusal != null, "refusal is not null");
                            if (refusal != null)
                            {
                                Check(refusal.Contains(AtomSpectraFirmware.WarningText("1.2.38")), "the refusal carries the firmware hint");
                            }
                            Check(Bm21StatusRequests() == 1, "one status request");
                        }
                        return true;
                    }
                case "bm21-ctl-ok":
                    {
                        string refusal;
                        bool ok = Bm21Begin(false, cmdPort, out refusal);
                        if (!bm21Na)
                        {
                            Check(ok, "the TCP Start succeeds");
                            Check(Bm21StatusRequests() == 0, "no status request on success");
                            Check(!(AtomSpectraTcpLog.StatusText ?? "").Contains("1.2.3"), "the status line carries no firmware hint");
                        }
                        return true;
                    }
                case "bm21-ctl-rest-ok":
                    {
                        string refusal;
                        bool ok = Bm21Begin(true, 0, out refusal);
                        if (!bm21Na)
                        {
                            Check(ok, "the REST Start succeeds");
                            Check(Bm21StatusRequests() == 0, "no status request on success");
                            Check(!(AtomSpectraTcpLog.StatusText ?? "").Contains("1.2.3"), "the status line carries no firmware hint");
                        }
                        return true;
                    }
                default:
                    return false;
            }
        }
    }
}
