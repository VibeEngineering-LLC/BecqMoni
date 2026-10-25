import sys, json, subprocess, pathlib
sys.stdout.reconfigure(encoding="utf-8")
import os; W = pathlib.Path(__file__).parent; T = pathlib.Path(os.environ["UI_MUT_TREE"]); P = T / "BecquerelMonitor"
MS = r"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin"
OUT = pathlib.Path(os.environ["UI_MUT_OUT"])
def sh(cmd, cwd=None):
    r = subprocess.run(cmd, capture_output=True, cwd=cwd, timeout=240); return r.returncode, (r.stdout + r.stderr).decode("utf-8", "replace")
for m in json.loads((W / sys.argv[1]).read_text(encoding="utf-8")):
    f = P / m["file"]; orig = f.read_bytes(); old = m["old"].replace("\r\n", "\n")
    raw = f.read_bytes().decode("utf-8-sig"); crlf = "\r\n" in raw
    txt = raw.replace("\r\n", "\n")
    if old not in txt:
        print("MUT", m["id"], "NOMATCH"); continue
    f.write_bytes(("\ufeff" + txt.replace(old, m["new"], 1).replace("\n", "\r\n" if crlf else "\n")).encode("utf-8"))
    try:
        rc, o = sh([MS + r"\MSBuild.exe", str(P / "BecquerelMonitor.csproj"), "-p:Configuration=Release", "-p:OutputPath=" + str(OUT) + "\\", "-nologo", "-v:q", "-restore", "-p:SignManifests=false", "-p:GenerateManifests=false"])
        if rc: print("MUT", m["id"], "BUILDERR", o[-300:].replace("\n", " ")); continue
        rc, o = sh([MS + r"\Roslyn\csc.exe", "-nologo", "-r:BecquerelMonitor.exe", "-r:WeifenLuo.WinFormsUI.Docking.dll", "-r:System.Windows.Forms.dll", "-r:System.Drawing.dll", "-out:UiCheck.exe", str(W / "UiSeriesCheck.cs"), str(W / "UiSeriesStage2.cs"), str(W / "UiSeriesStage3.cs"), str(W / "UiSeriesStage4.cs"), str(W / "UiSeriesStage5.cs"), str(W / "UiSeriesStage6.cs"), str(W / "UiSeriesStage7.cs")], cwd=OUT)
        rc, o = sh([sys.executable, str(W.parents[1] / "tools" / "run_hidden_desktop.py"), "--timeout", "150", "--cwd", str(OUT), "--", str(OUT / "UiCheck.exe")])   # NEVER on the operator desktop
        red = [l.split()[1].rstrip(":") for l in o.splitlines() if l.startswith("FAIL ")]
        print("MUT", m["id"], "KILLED" if red else "SURVIVED", "red=" + ",".join(red), "|", m["note"], flush=True)
    finally:
        f.write_bytes(orig)
