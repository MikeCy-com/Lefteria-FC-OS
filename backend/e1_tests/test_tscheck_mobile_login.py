from conftest import PLAYER, login, post
def test_demo_login_multi_role(client):
    assert post(client, "/mobile/auth/request-otp", json={"phone": PLAYER}).status_code == 200
    r = post(client, "/mobile/auth/verify-otp", json={"phone": PLAYER, "code": "123456"})
    assert r.status_code == 200
    roles = {i["role"] for i in r.json().get("identities", [])}
    assert {"player", "fan"} <= roles, r.text
def test_wrong_code_rejected(client):
    post(client, "/mobile/auth/request-otp", json={"phone": PLAYER})
    r = post(client, "/mobile/auth/verify-otp", json={"phone": PLAYER, "code": "000000", "role": "player"})
    assert r.status_code >= 400 and "token" not in r.text
def test_non_demo_phone_safe_failure(client):
    r = post(client, "/mobile/auth/request-otp", json={"phone": "+35799000000"})
    assert r.status_code in (400, 403, 422, 503) and r.json()["code"]
