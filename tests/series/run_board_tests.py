import argparse
import os
import shutil
import socket
import subprocess
import sys
import tempfile
import time
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
MOCKS = ROOT / "tests" / "atomspectra" / "mocks"
LAUNCHER = ROOT / "tools" / "run_hidden_desktop.py"
CSC = r"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"

SCENARIOS = [
    ("happy3", "tcp", []),
    ("userstop", "tcp", []),
    ("rst_fail", "tcp", ["--fail-rst", "1"]),
    ("sta_fail", "tcp", ["--fail-sta", "2"]),
    ("pause_link_lost", "tcp", []),
    ("pause_blip", "tcp", []),
    ("reset_outside", "tcp", ["--reset-at-time", "3"]),
    ("stop_question_lost", "tcp", ["--reset-at-time", "4"]),
    ("delayed_start", "tcp", []),
    ("counting_refuse", "tcp", ["--initial-counting", "1", "--initial-time", "50"]),
    ("counting_mid", "tcp", ["--resume-after-sto", "1", "--resume-delay", "0.5"]),
    ("foreign_data", "tcp", ["--initial-time", "100"]),
    ("drop_acquiring", "tcp", []),
    ("sto_fail", "tcp", ["--fail-sto", "1"]),
    ("windowed_control", "tcp", []),
    ("windowed_series", "tcp", []),
    ("happy3", "rest", []),
    ("userstop", "rest", []),
    ("rst_fail", "rest", ["--reset-fail-nth", "1"]),
    ("sta_fail", "rest", ["--sta-fail-nth", "2"]),
    ("pause_link_lost", "rest", []),
    ("pause_blip", "rest", []),
    ("reset_outside", "rest", ["--external-reset-at", "11"]),
    ("stop_question_lost", "rest", ["--external-reset-at", "10"]),
    ("delayed_start", "rest", []),
    ("counting_refuse", "rest", ["--counting", "1", "--time", "50", "--total", "1000", "--hist", "100", "--valid", "1"]),
    ("foreign_data", "rest", ["--counting", "0", "--time", "100", "--total", "5000", "--hist", "100", "--valid", "1"]),
    ("observe", "rest", []),
    ("windowed_series", "rest", []),
]


def parse_only(only_str):
    if not only_str:
        return None
    tokens = [t.strip() for t in only_str.split(",") if t.strip()]
    result = set()
    for t in tokens:
        if ":" in t:
            transport, name = t.split(":", 1)
            result.add((transport, name))
        else:
            result.add(("tcp", t))
            result.add(("rest", t))
    return result


def pick_free_port():
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


def wait_for_port(port, timeout=10.0):
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            s = socket.create_connection(("127.0.0.1", port), timeout=0.5)
            s.close()
            return True
        except (ConnectionRefusedError, OSError):
            time.sleep(0.1)
    return False


def main():
    parser = argparse.ArgumentParser(description="Run series-on-board tests")
    parser.add_argument("--exe", required=True, help="Directory with built BecquerelMonitor.exe and dlls")
    parser.add_argument("--work", default=None, help="Working directory")
    parser.add_argument("--only", default=None, help="Comma list of tcp:name / rest:name / name")
    parser.add_argument("--lang", default="ru", choices=["ru", "en"])
    parser.add_argument("--timeout", type=int, default=200, help="Hard limit of one BoardCheck run in seconds")
    args = parser.parse_args()

    if args.work:
        work = Path(args.work)
    else:
        work = Path(tempfile.gettempdir()) / f"bm_board_{os.getpid()}"

    work.mkdir(parents=True, exist_ok=True)

    # Copy the exe directory into work/app
    app_dir = work / "app"
    shutil.copytree(args.exe, app_dir, dirs_exist_ok=True)

    # Copy config if it exists
    config_src = ROOT / "config"
    if config_src.exists():
        shutil.copytree(config_src, app_dir / "config", dirs_exist_ok=True)

    # Compile BoardCheck.cs and BoardScenarios.cs
    file1 = HERE / "BoardCheck.cs"
    file2 = HERE / "BoardScenarios.cs"
    csc_cmd = [
        CSC,
        "-nologo",
        "-langversion:7.3",
        "-r:BecquerelMonitor.exe",
        "-r:WeifenLuo.WinFormsUI.Docking.dll",
        "-r:System.Windows.Forms.dll",
        "-r:System.Drawing.dll",
        "-out:BoardCheck.exe",
        str(file1),
        str(file2),
    ]
    try:
        build_proc = subprocess.run(
            csc_cmd,
            cwd=str(app_dir),
            capture_output=True,
            timeout=120,
        )
        build_out = build_proc.stdout.decode("cp866", errors="replace") + build_proc.stderr.decode("cp866", errors="replace")
        if build_proc.returncode != 0:
            print("BUILD FAIL")
            print(build_out)
            sys.exit(2)
    except Exception as e:
        print("BUILD FAIL")
        print(str(e))
        sys.exit(2)

    # Determine which scenarios to run
    only_set = parse_only(args.only)

    fail_count = 0
    total = 0

    for name, transport, stub_args in SCENARIOS:
        if only_set is not None and (transport, name) not in only_set:
            continue

        total += 1
        port = pick_free_port()
        scenario_dir = work / f"{transport}_{name}"
        scenario_dir.mkdir(parents=True, exist_ok=True)
        out_dir = scenario_dir / "out"
        out_dir.mkdir(exist_ok=True)
        log_file = scenario_dir / "stub.log"
        ctl_file = scenario_dir / "stub.ctl"
        log_file.touch()
        ctl_file.touch()

        # Build stub command
        if transport == "tcp":
            stub_cmd = [
                sys.executable, "-I",
                str(MOCKS / "mock_series_board.py"),
                "--port", str(port),
                "--log", str(log_file),
                "--ctl", str(ctl_file),
                "--duration", "400",
            ] + stub_args
        else:
            stub_cmd = [
                sys.executable, "-I",
                str(MOCKS / "mock_rest.py"),
                "--port", str(port),
                "--log", str(log_file),
                "--duration", "400",
                "--tick", "1.0", "--valid", "0",
            ] + stub_args

        creation_flags = 0
        if sys.platform == "win32":
            creation_flags = subprocess.CREATE_NO_WINDOW

        stub_proc = subprocess.Popen(
            stub_cmd,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            creationflags=creation_flags,
        )

        verdict = "FAIL"
        verdict_line = "FAIL no verdict"
        detail_lines = []

        try:
            if not wait_for_port(port, timeout=10.0):
                verdict = "FAIL"
                verdict_line = "FAIL stub did not start"
            else:
                # Run the harness through the hidden-desktop launcher
                launcher_cmd = [
                    sys.executable, str(LAUNCHER),
                    "--timeout", str(args.timeout),
                    "--cwd", str(app_dir),
                    "--",
                    str(app_dir / "BoardCheck.exe"),
                    name,
                    transport,
                    str(port),
                    str(log_file),
                    str(ctl_file),
                    str(out_dir),
                    args.lang,
                ]
                try:
                    run_proc = subprocess.run(
                        launcher_cmd,
                        capture_output=True,
                        timeout=args.timeout + 60,
                    )
                    output = (run_proc.stdout + run_proc.stderr).decode("utf-8", errors="replace")
                    lines = output.splitlines()

                    # Find the last line starting with "PASS " or "FAIL "
                    found_verdict = None
                    for line in reversed(lines):
                        if line.startswith("PASS ") or line.startswith("FAIL "):
                            found_verdict = line
                            break

                    if run_proc.returncode == 124:
                        verdict = "FAIL"
                        verdict_line = "FAIL timeout"
                    elif found_verdict:
                        if found_verdict.startswith("PASS "):
                            verdict = "PASS"
                        else:
                            verdict = "FAIL"
                        verdict_line = found_verdict
                    else:
                        verdict = "FAIL"
                        verdict_line = "FAIL no verdict"

                    # Collect detail lines for FAIL
                    if verdict == "FAIL":
                        for line in lines:
                            if line.startswith("  "):
                                detail_lines.append(line)

                except subprocess.TimeoutExpired:
                    verdict = "FAIL"
                    verdict_line = "FAIL timeout"
        finally:
            # Terminate the stub
            try:
                stub_proc.terminate()
                try:
                    stub_proc.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    stub_proc.kill()
                    stub_proc.wait()
            except Exception:
                pass
            # Send quit to ctl file for tcp
            if transport == "tcp":
                try:
                    with open(ctl_file, "a", encoding="utf-8") as f:
                        f.write("quit\n")
                except Exception:
                    pass

        # Print verdict
        print(f"{verdict_line} [{transport}]")
        if verdict == "FAIL":
            for dl in detail_lines:
                print(dl)

        print(f"RESULT {transport}:{name} {verdict}")
        if verdict == "FAIL":
            fail_count += 1

    print(f"SUMMARY scenarios={total} fail={fail_count}")
    sys.exit(1 if fail_count > 0 else 0)


if __name__ == "__main__":
    main()
