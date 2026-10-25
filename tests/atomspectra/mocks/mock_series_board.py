import argparse
import json
import os
import socket
import struct
import sys
import threading
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mock_board
import mock_cal

# Global state
state_lock = threading.Lock()
state = {
    'counting': False,
    't': 0,
    'data': False
}

# Command counters for fail options and logging
cmd_counters = {
    '-sta': 0,
    '-sto': 0,
    '-rst': 0
}
cmd_counter_lock = threading.Lock()

# Fail options
fail_config = {
    '-sta': 0,
    '-sto': 0,
    '-rst': 0
}

# Resume config
resume_config = {
    'n': 0,
    'delay': 2.0
}
resume_sto_count = 0
resume_lock = threading.Lock()

# Reset at time config
reset_at_time = -1
reset_done = False
reset_lock = threading.Lock()

# Log config
log_path = None
log_lock = threading.Lock()

# Control file config
ctl_path = None
ctl_offset = 0
down_until = 0.0   # the listener stays closed until this time (control line 'down MS')
ctl_lock = threading.Lock()

# Open connections
open_connections = set()
conn_lock = threading.Lock()

# Listener socket
listener_socket = None
listener_lock = threading.Lock()

# Base histogram
base_histogram = None

# Period
period = 0.5

# Duration
duration = 150.0

# Initial config
initial_time = 0
initial_counting = 0


def log_event(event_dict):
    if log_path is None:
        return
    try:
        with log_lock:
            with open(log_path, 'a') as f:
                f.write(json.dumps(event_dict) + '\n')
                f.flush()
    except Exception:
        pass


def get_histogram():
    """Get the current histogram based on state."""
    with state_lock:
        t = state['t']
        data = state['data']
    
    if t == 0 and not data:
        return [0] * 8192
    
    result = []
    for i in range(8192):
        val = (base_histogram[i] % 997) * t
        result.append(val)
    return result


def handle_command(sock, cmd, payload):
    """Handle a command and send response."""
    global resume_sto_count
    
    # Decode the command text
    try:
        cmd_text = payload.decode('ascii').strip()
    except Exception:
        return
    
    # Check if it's a command we handle
    if cmd_text in ('-sta', '-sto', '-rst'):
        # Increment counter
        with cmd_counter_lock:
            cmd_counters[cmd_text] += 1
            n = cmd_counters[cmd_text]
        
        # Check fail option
        if fail_config[cmd_text] > 0 and n == fail_config[cmd_text]:
            # No answer, not executed
            log_event({
                'cmd': cmd_text,
                'n': n,
                'answered': False,
                'board_time': state['t'],
                'counting': state['counting'],
                't': time.time()
            })
            return
        
        # Execute the command
        with state_lock:
            if cmd_text == '-sta':
                state['counting'] = True
                state['data'] = True
            elif cmd_text == '-sto':
                state['counting'] = False
                # Check resume
                with resume_lock:
                    if resume_config['n'] > 0:
                        resume_sto_count += 1
                        if resume_sto_count == resume_config['n']:
                            # Schedule resume
                            def do_resume():
                                time.sleep(resume_config['delay'])
                                with state_lock:
                                    state['counting'] = True
                                    state['data'] = True
                                log_event({
                                    'event': 'resume-by-itself',
                                    't': time.time()
                                })
                            threading.Thread(target=do_resume, daemon=True).start()
            elif cmd_text == '-rst':
                state['t'] = 0
                state['data'] = False
        
        # Get state for logging
        with state_lock:
            board_time = state['t']
            counting = state['counting']
        
        # Send response
        try:
            response = mock_board.encode(0x03, b"-ok\r\n")
            sock.sendall(response)
        except Exception:
            pass
        
        # Log
        log_event({
            'cmd': cmd_text,
            'n': n,
            'answered': True,
            'board_time': board_time,
            'counting': counting,
            't': time.time()
        })
    
    elif cmd_text in ('-inf', '-cal'):
        # Just answer, no state change
        try:
            response = mock_board.encode(0x03, b"-ok\r\n")
            sock.sendall(response)
        except Exception:
            pass


def reader_thread(sock, send_lock):
    """Read commands from the socket."""
    decoder = mock_cal.Decoder()
    try:
        while True:
            data = sock.recv(4096)
            if not data:
                break
            for byte in data:
                result = decoder.feed(byte)
                if result is not None:
                    cmd, payload = result
                    if cmd == 0x03:
                        handle_command(sock, cmd, payload)
    except Exception:
        pass


def streamer_thread(sock, send_lock):
    """Stream histogram and status packets."""
    try:
        while True:
            time.sleep(period)
            
            # Get histogram
            hist = get_histogram()
            
            # Send 8192//256 = 32 histogram packets
            for i in range(32):
                offset = i * 256
                values = hist[offset:offset + 256]
                payload = struct.pack('<H', offset) + struct.pack('<256I', *values)
                packet = mock_board.encode(0x01, payload)
                with send_lock:
                    sock.sendall(packet)
            
            # Send status packet
            with state_lock:
                t = state['t']
            status_payload = struct.pack('<IHIII', t, 7, 1234, 55, 0)
            status_packet = mock_board.encode(0x04, status_payload)
            with send_lock:
                sock.sendall(status_packet)
    except Exception:
        pass


def handle_connection(sock):
    """Handle a single client connection."""
    sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    send_lock = threading.Lock()
    
    # Add to open connections
    with conn_lock:
        open_connections.add(sock)
    
    log_event({'event': 'accept', 't': time.time()})
    
    # Start reader and streamer threads
    reader = threading.Thread(target=reader_thread, args=(sock, send_lock), daemon=True)
    streamer = threading.Thread(target=streamer_thread, args=(sock, send_lock), daemon=True)
    reader.start()
    streamer.start()
    
    # Wait for both threads to finish
    reader.join()
    streamer.join()
    
    # Close socket
    try:
        sock.close()
    except Exception:
        pass
    
    # Remove from open connections
    with conn_lock:
        open_connections.discard(sock)
    
    log_event({'event': 'close', 't': time.time()})


def ticker_thread():
    """Increment board time while counting."""
    global reset_done
    while True:
        time.sleep(period)
        with state_lock:
            if state['counting']:
                state['t'] += 1
                # Check reset-at-time
                if reset_at_time >= 0 and not reset_done and state['t'] >= reset_at_time:
                    state['t'] = 0
                    reset_done = True
                    log_event({'event': 'reset-from-outside', 't': time.time()})


def control_thread():
    """Poll the control file."""
    global ctl_offset, listener_socket, down_until
    while True:
        time.sleep(0.05)
        if ctl_path is None:
            continue
        try:
            with ctl_lock:
                if not os.path.exists(ctl_path):
                    continue
                with open(ctl_path, 'r') as f:
                    f.seek(ctl_offset)
                    data = f.read()
                    new_offset = f.tell()
            
            if not data:
                continue
            
            # Process lines
            lines = data.split('\n')
            # Keep the last incomplete line
            if lines and lines[-1] == '':
                lines = lines[:-1]
            
            for line in lines:
                line = line.strip()
                if not line:
                    continue
                
                if line == 'kill':
                    # Close all open client sockets
                    with conn_lock:
                        conns = list(open_connections)
                    for conn in conns:
                        try:
                            conn.close()
                        except Exception:
                            pass
                    log_event({'event': 'kill', 't': time.time()})
                
                elif line.startswith('down'):
                    parts = line.split()
                    if len(parts) >= 2:
                        try:
                            ms = int(parts[1])
                        except ValueError:
                            continue
                        # Close all client sockets
                        with conn_lock:
                            conns = list(open_connections)
                        for conn in conns:
                            try:
                                conn.close()
                            except Exception:
                                pass
                        # Close listener
                        with listener_lock:
                            if listener_socket:
                                try:
                                    listener_socket.close()
                                except Exception:
                                    pass
                                listener_socket = None
                        log_event({'event': 'down', 'ms': ms, 't': time.time()})
                        down_until = time.time() + ms / 1000.0   # the main loop reopens the listener after it
                
                elif line == 'quit':
                    os._exit(0)
            
            # Update offset
            with ctl_lock:
                ctl_offset = new_offset
        except Exception:
            pass


def main():
    global base_histogram, period, duration, initial_time, initial_counting
    global fail_config, resume_config, reset_at_time, log_path, ctl_path
    global listener_socket
    
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, required=True)
    parser.add_argument('--log', type=str, required=True)
    parser.add_argument('--period', type=float, default=0.5)
    parser.add_argument('--ctl', type=str, default=None)
    parser.add_argument('--duration', type=float, default=150)
    parser.add_argument('--initial-time', type=int, default=0)
    parser.add_argument('--initial-counting', type=int, default=0)
    parser.add_argument('--fail-rst', type=int, default=0)
    parser.add_argument('--fail-sta', type=int, default=0)
    parser.add_argument('--fail-sto', type=int, default=0)
    parser.add_argument('--reset-at-time', type=int, default=-1)
    parser.add_argument('--resume-after-sto', type=int, default=0)
    parser.add_argument('--resume-delay', type=float, default=2.0)
    
    args = parser.parse_args()
    
    # Set globals
    period = args.period
    duration = args.duration
    initial_time = args.initial_time
    initial_counting = args.initial_counting
    log_path = args.log
    ctl_path = args.ctl
    reset_at_time = args.reset_at_time
    fail_config['-rst'] = args.fail_rst
    fail_config['-sta'] = args.fail_sta
    fail_config['-sto'] = args.fail_sto
    resume_config['n'] = args.resume_after_sto
    resume_config['delay'] = args.resume_delay
    
    # Initialize base histogram
    base_histogram = mock_board.make_histogram(8192, 1)
    
    # Initialize state
    with state_lock:
        state['t'] = initial_time
        state['counting'] = (initial_counting == 1)
        if initial_time > 0:
            state['data'] = True
    
    # Start ticker thread
    ticker = threading.Thread(target=ticker_thread, daemon=True)
    ticker.start()
    
    # Start control thread
    control = threading.Thread(target=control_thread, daemon=True)
    control.start()
    
    # Create listener socket
    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    listener.bind(('127.0.0.1', args.port))
    listener.listen(5)
    listener.settimeout(0.2)
    
    with listener_lock:
        listener_socket = listener
    
    print(f"LISTENING 127.0.0.1:{args.port}", flush=True)
    
    # Accept loop
    start_time = time.time()
    while True:
        if time.time() - start_time > duration:
            break
        
        # Check if listener was closed (down command)
        with listener_lock:
            need_new = listener_socket is None
        if need_new:
            time.sleep(0.05)
            if time.time() < down_until:
                continue
            try:
                new_listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                new_listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
                new_listener.bind(('127.0.0.1', args.port))
                new_listener.listen(5)
                new_listener.settimeout(0.2)
                with listener_lock:
                    listener_socket = new_listener
                listener = new_listener
            except Exception:
                continue

        try:
            conn, addr = listener.accept()
            # Handle connection in a new thread
            threading.Thread(target=handle_connection, args=(conn,), daemon=True).start()
        except socket.timeout:
            continue
        except Exception:
            continue
    
    # Cleanup
    with conn_lock:
        conns = list(open_connections)
    for conn in conns:
        try:
            conn.close()
        except Exception:
            pass
    
    with listener_lock:
        if listener_socket:
            try:
                listener_socket.close()
            except Exception:
                pass
    
    sys.exit(0)


if __name__ == "__main__":
    main()
