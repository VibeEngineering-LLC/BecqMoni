import sys
import os
import json
import subprocess
import tempfile
import shutil
from pathlib import Path

def main():
    if len(sys.argv) < 2:
        print("Usage: run_board_mutations.py <mutations.json> [id,id,...]")
        sys.exit(1)

    sys.stdout.reconfigure(encoding="utf-8")

    HERE = Path(__file__).resolve().parent
    mut_file = Path(sys.argv[1])
    allowed_ids = None
    if len(sys.argv) > 2:
        allowed_ids = set(sys.argv[2].split(","))

    with open(mut_file, "r", encoding="utf-8") as f:
        mutations = json.load(f)

    UI_MUT_TREE = os.environ.get("UI_MUT_TREE")
    UI_MUT_OUT = os.environ.get("UI_MUT_OUT")
    if not UI_MUT_TREE or not UI_MUT_OUT:
        print("Error: UI_MUT_TREE and UI_MUT_OUT must be set")
        sys.exit(1)

    P = Path(UI_MUT_TREE) / "BecquerelMonitor"
    OUT = Path(UI_MUT_OUT)
    MS = r"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin"

    total = 0
    killed = 0
    survived = 0
    other = 0

    for m in mutations:
        mid = m["id"]
        if allowed_ids is not None and mid not in allowed_ids:
            continue

        total += 1
        f_path = P / m["file"]
        try:
            orig = f_path.read_bytes()
        except Exception:
            print(f"MUT {mid} NOMATCH")
            other += 1
            continue

        text = orig.decode("utf-8-sig")
        uses_crlf = "\r\n" in text
        text = text.replace("\r\n", "\n")
        old = m["old"].replace("\r\n", "\n")
        new = m["new"].replace("\r\n", "\n")

        if old not in text:
            print(f"MUT {mid} NOMATCH")
            other += 1
            continue

        mutated_text = text.replace(old, new, 1)
        if uses_crlf:
            mutated_text = mutated_text.replace("\n", "\r\n")
        f_path.write_bytes(("\ufeff" + mutated_text).encode("utf-8"))

        try:
            cmd = [
                MS + r"\MSBuild.exe",
                str(P / "BecquerelMonitor.csproj"),
                "-p:Configuration=Release",
                "-p:OutputPath=" + str(OUT) + "\\",
                "-nologo",
                "-v:q",
                "-restore",
                "-p:SignManifests=false",
                "-p:GenerateManifests=false"
            ]
            try:
                res = subprocess.run(cmd, capture_output=True, timeout=300)
            except subprocess.TimeoutExpired:
                print(f"MUT {mid} BUILDERR timeout")
                other += 1
                continue

            if res.returncode != 0:
                out_str = (res.stdout + res.stderr).decode("utf-8", errors="replace")
                last_300 = out_str[-300:].replace("\n", " ")
                print(f"MUT {mid} BUILDERR {last_300}")
                other += 1
                continue

            red = []

            if m.get("board"):
                work_dir = Path(tempfile.gettempdir()) / f"bm_mut_{mid}"
                if work_dir.exists():
                    shutil.rmtree(work_dir)
                work_dir.mkdir(parents=True)

                board_cmd = [
                    sys.executable,
                    "-I",
                    str(HERE / "run_board_tests.py"),
                    "--exe",
                    str(OUT),
                    "--work",
                    str(work_dir),
                    "--only",
                    ",".join(m["board"])
                ]
                try:
                    bres = subprocess.run(board_cmd, capture_output=True, timeout=1500)
                    bout = bres.stdout.decode("utf-8", errors="replace")
                    for line in bout.splitlines():
                        if line.startswith("RESULT ") and line.endswith(" FAIL"):
                            parts = line.split()
                            if len(parts) >= 2:
                                red.append(parts[1])
                except subprocess.TimeoutExpired:
                    pass

            if m.get("ui"):
                ui_files = [
                    "UiSeriesCheck.cs",
                    "UiSeriesStage2.cs",
                    "UiSeriesStage3.cs",
                    "UiSeriesStage4.cs",
                    "UiSeriesStage5.cs",
                    "UiSeriesStage6.cs",
                    "UiSeriesStage7.cs",
                    "UiSeriesStage8.cs"
                ]
                csc_cmd = [
                    MS + r"\Roslyn\csc.exe",
                    "-nologo",
                    "-langversion:7.3",
                    "-r:BecquerelMonitor.exe",
                    "-r:WeifenLuo.WinFormsUI.Docking.dll",
                    "-r:System.Windows.Forms.dll",
                    "-r:System.Drawing.dll",
                    "-out:UiCheck.exe"
                ]
                for uf in ui_files:
                    csc_cmd.append(str(HERE / uf))

                try:
                    subprocess.run(csc_cmd, capture_output=True, timeout=60, cwd=str(OUT))
                except subprocess.TimeoutExpired:
                    pass

                config_src = HERE.parents[1] / "config"
                if config_src.exists():
                    shutil.copytree(config_src, OUT / "config", dirs_exist_ok=True)

                ui_run_cmd = [
                    sys.executable,
                    str(HERE.parents[1] / "tools" / "run_hidden_desktop.py"),
                    "--timeout",
                    "180",
                    "--cwd",
                    str(OUT),
                    "--",
                    str(Path(OUT) / "UiCheck.exe")
                ]
                try:
                    ures = subprocess.run(ui_run_cmd, capture_output=True, timeout=180)
                    uout = ures.stdout.decode("utf-8", errors="replace")
                    for line in uout.splitlines():
                        if line.startswith("FAIL "):
                            parts = line.split()
                            if len(parts) >= 2:
                                name = parts[1].rstrip(":")
                                red.append(name)
                except subprocess.TimeoutExpired:
                    pass

            if red:
                print(f"MUT {mid} KILLED red={','.join(red)} | {m['note']}")
                killed += 1
            else:
                print(f"MUT {mid} SURVIVED | {m['note']}")
                survived += 1

        finally:
            f_path.write_bytes(orig)

    print(f"MUTATIONS total={total} killed={killed} survived={survived} other={other}")

if __name__ == "__main__":
    main()
