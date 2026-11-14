from conftest import COACH, login
def test_coach_sees_responses(client):
    h = {"Authorization": f"Bearer {login(COACH, 'coach')}"}
    r = client.get("/mobile/calendar", params={"month": "2026-10"}, headers=h); assert r.status_code == 200
    items = r.json()["items"]; assert items
    found = []
    for i in items:
        rr = client.get(f"/mobile/calendar/{i['kind']}/{i['id']}/responses", headers=h)
        assert rr.status_code == 200, rr.text
        if any(p.get("status") for p in rr.json()["players"]): found.append((i["title"], rr.text[:200]))
    print(found)
    assert found, "no RSVP replies visible to coach"
