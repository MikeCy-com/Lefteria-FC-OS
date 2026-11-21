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


def verify_provider_wiring(services):
    """Never expose credentials, and never accept a live Payabl switch."""
    api = services["api"].get("environment") or {}
    web = services["web"].get("environment") or {}
    keys = (
        "Payabl__Enabled", "Payabl__Sandbox", "Payabl__MerchantId",
        "Payabl__Secret", "Payabl__PublicOrigin",
        "TwilioPaymentSms__Enabled", "TwilioPaymentSms__AccountSid",
        "TwilioPaymentSms__AuthToken", "TwilioPaymentSms__MessagingServiceSid",
    )
    require(all(key in api for key in keys),
            "sandbox payment settings are not forwarded to API")

    def switch(key):
        value = str(api.get(key, "")).lower()
        require(value in ("true", "false"), key + " must be an explicit boolean")
        return value == "true"

    payment_enabled = switch("Payabl__Enabled")
    sms_enabled = switch("TwilioPaymentSms__Enabled")
    require(switch("Payabl__Sandbox"), "live Payabl configuration is not supported")
    require(not sms_enabled or payment_enabled,
            "payment-link SMS requires an approved sandbox gateway")

    if payment_enabled:
        from urllib.parse import urlsplit
        origin = str(api.get("Payabl__PublicOrigin", ""))
        expected = str(web.get("ORIGIN", ""))
        parts = urlsplit(origin)
        require(parts.scheme == "https" and bool(parts.hostname)
                and not parts.username and not parts.password
                and not parts.query and not parts.fragment
                and parts.path in ("", "/")
                and origin.rstrip("/") == expected.rstrip("/"),
                "sandbox callback origin must match the public HTTPS site")
        require(bool(str(api.get("Payabl__MerchantId", "")))
                and len(str(api.get("Payabl__Secret", ""))) >= 16,
                "sandbox Payabl merchant credentials are not configured")
    if sms_enabled:
        require(bool(str(api.get("TwilioPaymentSms__AccountSid", "")))
                and bool(str(api.get("TwilioPaymentSms__MessagingServiceSid", "")))
                and len(str(api.get("TwilioPaymentSms__AuthToken", ""))) >= 20,
                "sandbox payment SMS service credentials are not configured")


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
    verify_provider_wiring(services)
    print("Production Compose least-privilege configuration verified.")


if __name__ == "__main__":
    main()
