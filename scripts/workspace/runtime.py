#!/usr/bin/env python3
"""Workspace runtime manager: runs PostgreSQL and the .NET demo API host as managed children.

Started (detached from uvicorn reloads) by backend/server.py. A single instance is
enforced by an flock. It exits, stopping its children, when the watched launcher
process disappears or on SIGTERM/SIGINT.
"""
import fcntl
import os
import signal
import socket
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent


def read_env(path):
    values = {}
    if path.exists():
        for line in path.read_text().splitlines():
            if "=" in line and not line.lstrip().startswith("#"):
                key, value = line.split("=", 1)
                values[key.strip()] = value.strip()
    return values


CONF = read_env(HERE / "workspace.conf")
RT = Path(CONF["LEFTERIA_RUNTIME_DIR"])
REPO = Path(CONF["LEFTERIA_REPO"])
PGBIN = Path(f"/usr/lib/postgresql/{CONF['LEFTERIA_PG_MAJOR']}/bin")
DOTNET = Path(CONF["LEFTERIA_TOOLS_DIR"]) / "dotnet"
TOOL_DLL = REPO / "backend/tools/LefteriaFc.MobilePreview/bin/Release/net10.0/LefteriaFc.MobilePreview.dll"
PG_PORT = int(CONF["LEFTERIA_PG_PORT"])
API_PORT = int(CONF["LEFTERIA_API_PORT"])

stopping = False


def log(message):
    print(f"[runtime {time.strftime('%H:%M:%S')}] {message}", flush=True)


def alive(pid):
    try:
        os.kill(pid, 0)
        return True
    except ProcessLookupError:
        return False
    except PermissionError:
        return True


def port_open(port):
    with socket.socket() as sock:
        sock.settimeout(0.5)
        return sock.connect_ex(("127.0.0.1", port)) == 0


def bootstrap(step):
    return subprocess.run(["bash", str(HERE / "bootstrap.sh"), step]).returncode == 0


def start_postgres():
    (RT / "run/pg").mkdir(parents=True, exist_ok=True)
    return subprocess.Popen(
        [str(PGBIN / "postgres"), "-D", str(RT / "pgdata"), "-p", str(PG_PORT),
         "-c", "listen_addresses=127.0.0.1", "-c", f"unix_socket_directories={RT / 'run/pg'}",
         "-c", "logging_collector=off"],
        user="postgres", group="postgres", extra_groups=[], cwd="/",
        stdout=open(RT / "logs/postgres.log", "ab"), stderr=subprocess.STDOUT)


def pg_ready():
    return subprocess.run([str(PGBIN / "pg_isready"), "-q", "-h", "127.0.0.1", "-p", str(PG_PORT)]).returncode == 0


def start_api():
    secrets = read_env(RT / "workspace.env")
    env = {
        "PATH": f"{DOTNET}:/usr/local/bin:/usr/bin:/bin", "HOME": os.environ.get("HOME", "/root"),
        "DOTNET_ROOT": str(DOTNET), "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1",
        "LEFTERIA_TEST_FIXTURE": str(RT / "identity-db.json"),
        "LEFTERIA_WORKSPACE_STATE": str(RT / "state/demo-state.json"),
        "LEFTERIA_WORKSPACE_API_PORT": str(API_PORT),
        "LEFTERIA_WORKSPACE_PUBLIC_ORIGIN": CONF["LEFTERIA_PUBLIC_ORIGIN"],
        "LEFTERIA_WORKSPACE_KNOWN_PROXY": "127.0.0.1",
        "LEFTERIA_PUBLIC_MEDIA_ROOT": str(RT / "public-media"),
    }
    for key in ("MOBILE_SIGNING_KEY", "MOBILE_OTP_KEY", "WEB_ADMIN_THROTTLE_KEY", "WEB_ADMIN_FOUNDING_SETUP_CODE"):
        env[key] = secrets[key]
    return subprocess.Popen([str(DOTNET / "dotnet"), str(TOOL_DLL), "--workspace", "serve"], cwd=str(REPO), env=env,
                            stdout=open(RT / "logs/api.log", "ab"), stderr=subprocess.STDOUT)


def stop_child(proc, sig, timeout):
    if proc is None or proc.poll() is not None:
        return
    try:
        proc.send_signal(sig)
        proc.wait(timeout)
    except subprocess.TimeoutExpired:
        proc.kill()
        proc.wait()
    except ProcessLookupError:
        pass


def wait_until(predicate, seconds, watch):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline and not stopping and alive(watch):
        if predicate():
            return True
        time.sleep(0.25)
    return False


def main():
    global stopping
    watch = int(sys.argv[1]) if len(sys.argv) > 1 else os.getppid()
    for directory in ("run", "logs", "state"):
        (RT / directory).mkdir(parents=True, exist_ok=True)
    lock = open(RT / "run/runtime.lock", "w")
    try:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        return 0
    lock.write(str(os.getpid()))
    lock.flush()

    def on_signal(*_):
        global stopping
        stopping = True

    signal.signal(signal.SIGTERM, on_signal)
    signal.signal(signal.SIGINT, on_signal)
    started = time.monotonic()
    postgres = api = None
    failures = 0
    try:
        while not stopping and alive(watch):
            if postgres is None or postgres.poll() is not None:
                stop_child(api, signal.SIGTERM, 15)
                api = None
                if not bootstrap("prepare"):
                    log("prepare failed; retrying in 10s")
                    time.sleep(10)
                    continue
                if port_open(PG_PORT):
                    log(f"port {PG_PORT} is busy; waiting for the previous PostgreSQL to stop")
                    time.sleep(2)
                    continue
                postgres = start_postgres()
                if not wait_until(pg_ready, 60, watch):
                    log("PostgreSQL did not become ready")
                    stop_child(postgres, signal.SIGINT, 20)
                    postgres = None
                    time.sleep(5)
                    continue
                if not bootstrap("provision"):
                    log("provision failed; retrying in 10s")
                    stop_child(postgres, signal.SIGINT, 20)
                    postgres = None
                    time.sleep(10)
                    continue
            if api is None or api.poll() is not None:
                if api is not None:
                    failures += 1
                    log(f"API host exited with {api.returncode}; restarting (see {RT / 'logs/api.log'})")
                    time.sleep(min(30, 2 * failures))
                api = start_api()
                if wait_until(lambda: port_open(API_PORT) or api.poll() is not None, 120, watch) and api.poll() is None:
                    failures = 0
                    log(f"API host ready on 127.0.0.1:{API_PORT} after {time.monotonic() - started:.1f}s")
            time.sleep(1)
    finally:
        log("stopping API host and PostgreSQL")
        stop_child(api, signal.SIGTERM, 20)
        stop_child(postgres, signal.SIGINT, 25)
    return 0


if __name__ == "__main__":
    sys.exit(main())
