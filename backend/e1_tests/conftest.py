import httpx, pytest, time, re

def post(c, url, **kw):
    for _ in range(6):
        r = c.post(url, **kw)
        if r.status_code != 429: return r
        m = re.search(r'(\d+)s', r.text); time.sleep(int(m.group(1)) + 1 if m else 15)
    return r
BASE = "http://localhost:8001/api"
PLAYER, PARENT, COACH = "+999508474740001", "+999508474740002", "+999508474740003"

def login(phone, role):
    with httpx.Client(base_url=BASE, timeout=30) as c:
        r = post(c, "/mobile/auth/request-otp", json={"phone": phone}); assert r.status_code == 200, r.text
        r = post(c, "/mobile/auth/verify-otp", json={"phone": phone, "code": "123456", "role": role})
        assert r.status_code == 200, r.text
        return r.json()["token"]

@pytest.fixture
def client():
    with httpx.Client(base_url=BASE, timeout=30) as c:
        yield c
