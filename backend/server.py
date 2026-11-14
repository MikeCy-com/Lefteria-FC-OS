"""ASGI bridge for the fixed workspace launcher (uvicorn server:app on :8001).

Streams every /api/* request to the loopback .NET API host managed by
scripts/workspace/runtime.py. Returns 503 while that host is not ready.
"""
import asyncio
import fcntl
import json
import os
import subprocess
import sys
from pathlib import Path

import httpx
from starlette.applications import Starlette
from starlette.requests import Request
from starlette.responses import FileResponse, JSONResponse, Response, StreamingResponse
from starlette.routing import Route

REPO = Path(__file__).resolve().parent.parent
WORKSPACE = REPO / "scripts/workspace"
CONF = dict(line.split("=", 1) for line in (WORKSPACE / "workspace.conf").read_text().split() if "=" in line)
RUNTIME = Path(CONF["LEFTERIA_RUNTIME_DIR"])
UPSTREAM = f"http://127.0.0.1:{CONF['LEFTERIA_API_PORT']}"
OPENAPI = REPO / "contracts/openapi.json"
HOP = {"connection", "keep-alive", "proxy-authenticate", "proxy-authorization", "te", "trailer",
       "transfer-encoding", "upgrade", "forwarded", "x-forwarded-for", "x-forwarded-proto", "x-forwarded-host",
       "x-forwarded-port", "x-real-ip"}

client: httpx.AsyncClient | None = None


def unavailable():
    return JSONResponse({"detail": "The API host is not ready yet.", "code": "api_unavailable"}, status_code=503,
                        headers={"Cache-Control": "no-store", "Retry-After": "5"})


def launcher_pid():
    parent = os.getppid()
    try:
        if b"uvicorn" in Path(f"/proc/{parent}/cmdline").read_bytes():
            return parent
    except OSError:
        pass
    return os.getpid()


def runtime_running():
    (RUNTIME / "run").mkdir(parents=True, exist_ok=True)
    with open(RUNTIME / "run/runtime.lock", "a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            return True
        fcntl.flock(lock, fcntl.LOCK_UN)
        return False


async def keep_runtime():
    # Reuses a running manager across uvicorn --reload; starts one only when none holds the lock.
    (RUNTIME / "logs").mkdir(parents=True, exist_ok=True)
    while True:
        if not runtime_running():
            subprocess.Popen([sys.executable, str(WORKSPACE / "runtime.py"), str(launcher_pid())],
                             stdin=subprocess.DEVNULL, stdout=open(RUNTIME / "logs/runtime.log", "ab"),
                             stderr=subprocess.STDOUT, cwd=str(REPO))
        await asyncio.sleep(3)


async def startup():
    global client
    client = httpx.AsyncClient(base_url=UPSTREAM, timeout=httpx.Timeout(120, connect=2), follow_redirects=False,
                               limits=httpx.Limits(max_connections=200, max_keepalive_connections=50))
    app.state.runtime_task = asyncio.create_task(keep_runtime())


async def shutdown():
    app.state.runtime_task.cancel()
    await client.aclose()


def forwarded_headers(request: Request):
    headers = [(k, v) for k, v in request.headers.raw if k.decode("latin-1").lower() not in HOP]
    original_for = request.headers.get("x-forwarded-for", "")
    hops = [part.strip() for part in original_for.split(",") if part.strip()]
    # Ingress chain appends a fixed number of trusted hops (CDN, load balancer) after the real client.
    trusted = int(CONF.get("LEFTERIA_BRIDGE_XFF_TRUSTED_HOPS", "0"))
    if len(hops) > trusted:
        client_ip = hops[-(trusted + 1)]
    else:
        client_ip = request.client.host if request.client else "127.0.0.1"
    proto = request.headers.get("x-forwarded-proto", request.url.scheme).split(",")[0].strip()
    host = request.headers.get("x-forwarded-host", request.headers.get("host", "")).split(",")[0].strip()
    headers += [(b"x-forwarded-for", client_ip.encode()), (b"x-forwarded-proto", proto.encode()),
                (b"x-forwarded-host", host.encode())]
    return headers


async def openapi(_: Request):
    return FileResponse(OPENAPI, media_type="application/json")


async def proxy(request: Request):
    path = request.url.path
    query = request.url.query
    target = path + ("?" + query if query else "")
    has_body = request.method not in ("GET", "HEAD") or "content-length" in request.headers or \
        "transfer-encoding" in request.headers
    upstream_request = client.build_request(request.method, target, headers=forwarded_headers(request),
                                            content=request.stream() if has_body else None)
    try:
        upstream = await client.send(upstream_request, stream=True)
    except (httpx.ConnectError, httpx.ConnectTimeout, httpx.RemoteProtocolError):
        return unavailable()
    raw = [(k, v) for k, v in upstream.headers.raw if k.decode("latin-1").lower() not in HOP]

    async def body():
        try:
            async for chunk in upstream.aiter_raw():
                yield chunk
        finally:
            await upstream.aclose()

    if request.method == "HEAD":
        await upstream.aclose()
        response = Response(status_code=upstream.status_code)
    else:
        response = StreamingResponse(body(), status_code=upstream.status_code)
    response.raw_headers = raw
    return response


METHODS = ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"]
app = Starlette(routes=[Route("/api/openapi.json", openapi, methods=["GET", "HEAD"]),
                        Route("/api/{rest:path}", proxy, methods=METHODS),
                        Route("/api", proxy, methods=METHODS)],
                on_startup=[startup], on_shutdown=[shutdown])
