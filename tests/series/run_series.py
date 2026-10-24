import sys; sys.stdout.reconfigure(encoding="utf-8")
import os, json, subprocess, tempfile, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
PROD = os.path.join(REPO, "BecquerelMonitor")
PROD_FILES = ["SeriesSettings.cs", "MeasurementSeries.cs"]
TEST_FILE = os.path.join(HERE, "SeriesTests.cs")
MUTATIONS_FILE = os.path.join(HERE, "mutations.json")

def find_csc():
    if os.environ.get("SERIES_CSC"):
        return os.environ["SERIES_CSC"]
    vswhere = os.path.join(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"), "Microsoft Visual Studio", "Installer", "vswhere.exe")
    if os.path.exists(vswhere):
        try:
            out = subprocess.run([vswhere, "-latest", "-products", "*", "-requires", "Microsoft.Component.MSBuild", "-find", r"MSBuild\**\Bin\MSBuild.exe"], capture_output=True, timeout=30).stdout.decode(errors="replace")
            for line in out.splitlines():
                line = line.strip()
                if line and os.path.exists(line):
                    csc = os.path.join(os.path.dirname(os.path.dirname(line)), "Roslyn", "csc.exe")
                    if os.path.exists(csc):
                        return csc
        except Exception:
            pass
    return r"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"

CSC = find_csc()

def compile(sources, exe):
    cmd = [CSC, "-nologo", "-out:" + exe] + sources
    p = subprocess.run(cmd, capture_output=True)
    text = (p.stdout.decode(errors="replace") + p.stderr.decode(errors="replace"))
    return (p.returncode == 0, text)

def run(exe):
    p = subprocess.run([exe], capture_output=True, timeout=120)
    text = p.stdout.decode(errors="replace") + p.stderr.decode(errors="replace")
    fails, passes = [], []
    for line in text.splitlines():
        parts = line.split()
        if len(parts) >= 2:
            name = parts[1].rstrip(":")
            if line.startswith("FAIL "):
                fails.append(name)
            elif line.startswith("PASS "):
                passes.append(name)
    return (fails, passes, p.returncode, text)

def apply_patch(text, old, new, occurrence=1):
    text = text.replace("\r\n", "\n")
    old = old.replace("\r\n", "\n")
    new = new.replace("\r\n", "\n")
    idx = -1
    for _ in range(occurrence):
        idx = text.find(old, idx + 1)
        if idx == -1:
            raise ValueError("no match")
    return text[:idx] + new + text[idx + len(old):]

def do_baseline():
    tmp = tempfile.mkdtemp()
    try:
        exe = os.path.join(tmp, "test.exe")
        sources = [os.path.join(PROD, f) for f in PROD_FILES] + [TEST_FILE]
        ok, text = compile(sources, exe)
        if not ok:
            print(text)
            return False
        fails, passes, rc, raw = run(exe)
        print(raw)
        return len(passes) > 0 and len(fails) == 0
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

def main():
    if len(sys.argv) > 1 and sys.argv[1] == "--mutations":
        if not do_baseline():
            print("BASELINE RED")
            sys.exit(2)
        with open(MUTATIONS_FILE, encoding="utf-8") as f:
            mutations = json.load(f)
        stats = {"killed": 0, "survived": 0, "nomatch": 0, "compile_error": 0, "owner_red": 0, "only_owner": 0}
        all_ok = True
        for m in mutations:
            tmp = tempfile.mkdtemp()
            try:
                fname = m["file"]
                src_path = os.path.join(PROD, fname)
                with open(src_path, encoding="utf-8-sig") as f:
                    content = f.read()
                try:
                    patched = apply_patch(content, m["old"], m["new"], m.get("occurrence", 1))
                except ValueError:
                    cls = "NO MATCH"
                    stats["nomatch"] += 1
                    all_ok = False
                    print(f"MUT {m['id']} {cls} red=- owner={m['owner']} owner_red=no only_owner=no | {m['note']}", flush=True)
                    continue
                dst_path = os.path.join(tmp, fname)
                with open(dst_path, "w", encoding="utf-8") as f:
                    f.write(patched)
                other = [f for f in PROD_FILES if f != fname]
                sources = [os.path.join(tmp, fname)] + [os.path.join(PROD, f) for f in other] + [TEST_FILE]
                exe = os.path.join(tmp, "test.exe")
                ok, text = compile(sources, exe)
                if not ok:
                    cls = "COMPILE ERROR"
                    stats["compile_error"] += 1
                    all_ok = False
                    lines = text.splitlines()[:5]
                    print(f"MUT {m['id']} {cls} red=- owner={m['owner']} owner_red=no only_owner=no | {m['note']} | {' '.join(lines)}", flush=True)
                    continue
                fails, passes, rc, raw = run(exe)
                owner = m["owner"]
                owner_red = any(n.startswith(owner) for n in fails)
                only_owner = len(fails) == 1 and fails[0].startswith(owner)
                if len(fails) > 0:
                    cls = "KILLED"
                    stats["killed"] += 1
                    if owner_red:
                        stats["owner_red"] += 1
                    if only_owner:
                        stats["only_owner"] += 1
                    if not owner_red:
                        all_ok = False
                else:
                    cls = "SURVIVED"
                    stats["survived"] += 1
                    all_ok = False
                red_str = ",".join(fails) if fails else "-"
                print(f"MUT {m['id']} {cls} red={red_str} owner={owner} owner_red={'yes' if owner_red else 'no'} only_owner={'yes' if only_owner else 'no'} | {m['note']}", flush=True)
            finally:
                shutil.rmtree(tmp, ignore_errors=True)
        n = len(mutations)
        print(f"SUMMARY mutations={n} killed={stats['killed']} survived={stats['survived']} nomatch={stats['nomatch']} compile_error={stats['compile_error']} owner_red={stats['owner_red']} only_owner={stats['only_owner']}")
        sys.exit(0 if all_ok else 1)
    else:
        sys.exit(0 if do_baseline() else 1)

if __name__ == "__main__":
    main()
