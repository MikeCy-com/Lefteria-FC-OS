def test_health_and_openapi(client):
    assert client.get("/health/live").status_code == 200
    assert client.get("/health/ready").status_code == 200
    r = client.get("/openapi.json"); assert r.status_code == 200
    p = r.json()["paths"]
    assert len(p) == 172
    assert "/api/mobile/auth/request-otp" in p and "/api/admin/auth/setup" in p
