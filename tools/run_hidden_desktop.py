import sys
import os
import time
import ctypes
import ctypes.wintypes
import subprocess
import tempfile
import argparse

def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    
    parser = argparse.ArgumentParser()
    parser.add_argument("--timeout", type=int, default=120)
    parser.add_argument("--cwd", type=str, default=None)
    parser.add_argument("--show-desktop", action="store_true")
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    
    if args.command and args.command[0] == "--":
        args.command = args.command[1:]
    
    if not args.command:
        print("ERROR: No command provided", file=sys.stderr)
        sys.exit(1)
    
    # Setup Win32 API
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    
    # Define structures
    class STARTUPINFOW(ctypes.Structure):
        _fields_ = [
            ("cb", ctypes.wintypes.DWORD),
            ("lpReserved", ctypes.wintypes.LPWSTR),
            ("lpDesktop", ctypes.wintypes.LPWSTR),
            ("lpTitle", ctypes.wintypes.LPWSTR),
            ("dwX", ctypes.wintypes.DWORD),
            ("dwY", ctypes.wintypes.DWORD),
            ("dwXSize", ctypes.wintypes.DWORD),
            ("dwYSize", ctypes.wintypes.DWORD),
            ("dwXCountChars", ctypes.wintypes.DWORD),
            ("dwYCountChars", ctypes.wintypes.DWORD),
            ("dwFillAttribute", ctypes.wintypes.DWORD),
            ("dwFlags", ctypes.wintypes.DWORD),
            ("wShowWindow", ctypes.wintypes.WORD),
            ("cbReserved2", ctypes.wintypes.WORD),
            ("lpReserved2", ctypes.POINTER(ctypes.c_byte)),
            ("hStdInput", ctypes.wintypes.HANDLE),
            ("hStdOutput", ctypes.wintypes.HANDLE),
            ("hStdError", ctypes.wintypes.HANDLE),
        ]
        
    class PROCESS_INFORMATION(ctypes.Structure):
        _fields_ = [
            ("hProcess", ctypes.wintypes.HANDLE),
            ("hThread", ctypes.wintypes.HANDLE),
            ("dwProcessId", ctypes.wintypes.DWORD),
            ("dwThreadId", ctypes.wintypes.DWORD),
        ]
        
    # Set argtypes
    user32.CreateDesktopW.argtypes = [ctypes.c_wchar_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.wintypes.DWORD, ctypes.wintypes.DWORD, ctypes.c_void_p]
    user32.CreateDesktopW.restype = ctypes.c_void_p
    user32.CloseDesktop.argtypes = [ctypes.c_void_p]
    user32.CloseDesktop.restype = ctypes.wintypes.BOOL
    
    kernel32.CreateProcessW.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.wintypes.BOOL, ctypes.wintypes.DWORD, ctypes.c_void_p, ctypes.c_wchar_p, ctypes.POINTER(STARTUPINFOW), ctypes.POINTER(PROCESS_INFORMATION)]
    kernel32.CreateProcessW.restype = ctypes.wintypes.BOOL
    kernel32.WaitForSingleObject.argtypes = [ctypes.wintypes.HANDLE, ctypes.wintypes.DWORD]
    kernel32.WaitForSingleObject.restype = ctypes.wintypes.DWORD
    kernel32.GetExitCodeProcess.argtypes = [ctypes.wintypes.HANDLE, ctypes.POINTER(ctypes.wintypes.DWORD)]
    kernel32.GetExitCodeProcess.restype = ctypes.wintypes.BOOL
    kernel32.CloseHandle.argtypes = [ctypes.wintypes.HANDLE]
    kernel32.CloseHandle.restype = ctypes.wintypes.BOOL
    
    # Create Desktop
    desktop_name = "bm_hidden_" + str(os.getpid()) + "_" + str(int(time.time()))
    GENERIC_ALL = 0x10000000
    hDesktop = user32.CreateDesktopW(desktop_name, None, None, 0, GENERIC_ALL, None)
    
    if not hDesktop:
        err = ctypes.get_last_error()
        print(f"ERROR: CreateDesktop failed: {err}", file=sys.stderr)
        sys.exit(3)
        
    if args.show_desktop:
        print(desktop_name)
        
    # Prepare command line
    # Quote every argument
    quoted_args = []
    for arg in args.command:
        quoted_args.append('"' + arg + '"')
        
    # Create temp file for output
    fd, out_file = tempfile.mkstemp()
    os.close(fd)
    
    # Build cmd.exe command
    # cmd.exe /d /s /c "<exe> <args> > <out_file> 2>&1"
    # Note: The prompt says "quote every argument with double quotes".
    # The structure is: cmd.exe /d /s /c " <exe> <args> > <out_file> 2>&1 "
    # We need to be careful with quoting inside the cmd /c string.
    # Standard way: cmd /c "program arg1 arg2 > out 2>&1"
    # If program has spaces, it needs quotes.
    # Let's construct the inner command string.
    inner_cmd = " ".join(quoted_args)
    # The redirection part
    redirect_part = f" > \"{out_file}\" 2>&1"
    full_inner = inner_cmd + redirect_part
    
    # The full command line for CreateProcessW
    # cmd.exe /d /s /c "full_inner"
    # If full_inner contains quotes, we might need to escape them for cmd, 
    # but the prompt says "embedded quotes are not expected".
    # However, our quoted_args contain quotes.
    # cmd /c " "exe" "arg" > out " "
    # This is tricky. Let's follow the prompt's specific instruction:
    # "cmd.exe /d /s /c \"<quoted exe and args> > <quoted out file> 2>&1\""
    # This implies the string passed to /c is: " "exe" "arg" > "out" 2>&1 "
    
    lpCommandLine = ctypes.create_unicode_buffer(f'cmd.exe /d /s /c "{full_inner}"')
    
    si = STARTUPINFOW()
    si.cb = ctypes.sizeof(STARTUPINFOW)
    si.lpDesktop = desktop_name
    
    pi = PROCESS_INFORMATION()
    
    # Create Process
    # lpApplicationName is None, lpCommandLine is the buffer
    # bInheritHandles is False
    # dwCreationFlags is 0
    # lpEnvironment is None
    # lpCurrentDirectory is args.cwd
    success = kernel32.CreateProcessW(
        None,
        lpCommandLine,
        None,
        None,
        False,
        0,
        None,
        args.cwd,
        ctypes.byref(si),
        ctypes.byref(pi)
    )
    
    if not success:
        err = ctypes.get_last_error()
        print(f"ERROR: CreateProcess failed: {err}", file=sys.stderr)
        os.remove(out_file)
        user32.CloseDesktop(hDesktop)
        sys.exit(1)
        
    hProcess = pi.hProcess
    hThread = pi.hThread
    pid = pi.dwProcessId
    
    timed_out = False
    exit_code = 1
    
    try:
        # Wait for process
        timeout_ms = args.timeout * 1000
        result = kernel32.WaitForSingleObject(hProcess, timeout_ms)
        
        if result == 0x102: # WAIT_TIMEOUT
            timed_out = True
            # Kill tree
            subprocess.run(["taskkill", "/F", "/T", "/PID", str(pid)], capture_output=True)
            # Wait up to 5s more
            kernel32.WaitForSingleObject(hProcess, 5000)
        else:
            # Get exit code
            exit_code_val = ctypes.wintypes.DWORD()
            kernel32.GetExitCodeProcess(hProcess, ctypes.byref(exit_code_val))
            exit_code = exit_code_val.value
            
    finally:
        # Close handles
        kernel32.CloseHandle(hProcess)
        kernel32.CloseHandle(hThread)
        user32.CloseDesktop(hDesktop)
        
    # Read output
    try:
        with open(out_file, "rb") as f:
            data = f.read()
        text = data.decode("cp866", errors="replace")
        sys.stdout.write(text)
        sys.stdout.flush()
    except Exception as e:
        print(f"ERROR: Failed to read output: {e}", file=sys.stderr)
        
    # Cleanup temp file
    try:
        os.remove(out_file)
    except OSError:
        pass
        
    if timed_out:
        print(f"HIDDEN-DESKTOP TIMEOUT after {args.timeout} s: the process was killed")
        sys.exit(124)
    else:
        sys.exit(exit_code)

if __name__ == "__main__":
    main()
