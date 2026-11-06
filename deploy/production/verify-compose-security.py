#!/usr/bin/env python3
"""Assert the fully resolved production Compose model keeps runtime isolation.

Read JSON from docker compose config --format json on stdin. Never print the
configuration; it can contain synthetic credentials in CI or real ones locally.
"""
import json
import sys

SERVICES = {"proxy", "web", "api", "postgres", "migrate"}
EXCEPTIONS = {
    "proxy": {"NET_BIND_SERVICE"},
    "postgres": {"CHOWN", "DAC_OVERRIDE", "FOWNER", "SETGID", "SETUID"},
}


def require(ok, message):
    if not ok:
        raise SystemExit("Production Compose security check failed: " + message)


def main():
    configuration = json.load(sys.stdin)
    services = configuration.get("services", {})
    require(SERVICES <= services.keys(), "missing expected services")
    require(bool(configuration.get("networks", {}).get("app", {}).get("internal")),
            "application network must remain internal")
    require(bool(configuration.get("networks", {}).get("data", {}).get("internal")),
            "database network must remain internal")
    for name in sorted(SERVICES):
        service = services[name]
        require(service.get("read_only") is True, name + ": root filesystem is writable")
        require(service.get("init") is True, name + ": init must be enabled")
        require("ALL" in service.get("cap_drop", []), name + ": Linux capabilities not dropped")
        actual = set(service.get("cap_add", []))
        require(actual == EXCEPTIONS.get(name, set()),
                name + ": unexpected additional/missing Linux capabilities")
        security = [str(item).replace("=", ":") for item in service.get("security_opt", [])]
        require("no-new-privileges:true" in security,
                name + ": no-new-privileges missing")
        require(bool(service.get("tmpfs")), name + ": writable temporary directory missing")
        require(bool(service.get("mem_limit")), name + ": memory cap missing")
        require(float(service.get("cpus", 0)) > 0, name + ": CPU quota missing")
        require(int(service.get("pids_limit", 0)) > 0, name + ": PID cap missing")
        logging = service.get("logging") or {}
        require(logging.get("driver") == "json-file", name + ": bounded json-file logging missing")
        options = logging.get("options") or {}
        require(bool(options.get("max-size")) and bool(options.get("max-file")),
                name + ": log rotation options missing")
        if name != "proxy":
            require(not service.get("ports"), name + ": private service publishes a host port")
    require(services["migrate"].get("restart") == "no", "migration tool must not auto-restart")
    require("tools" in services["migrate"].get("profiles", []),
            "migration tool must be opt-in")
    require("public" in services["proxy"].get("networks", {}),
            "only edge service should bind a public network")
    print("Production Compose least-privilege configuration verified.")


if __name__ == "__main__":
    main()
