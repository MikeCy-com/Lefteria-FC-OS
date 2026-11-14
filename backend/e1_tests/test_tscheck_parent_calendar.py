from conftest import PARENT, login
def test_parent_children_scope(client):
    h = {"Authorization": f"Bearer {login(PARENT, 'parent')}"}
    r = client.get("/mobile/calendar", params={"month": "2026-10"}, headers=h); assert r.status_code == 200
    d = r.json(); names = {p["name"] for p in d["players"]}
    assert names == {"Fictional child", "Fictional FC child", "Fictional second child"}
    titles_by_child = {}
    for p in d["players"]:
        rr = client.get("/mobile/calendar", params={"month": "2026-10", "player_id": p["id"]}, headers=h)
        assert rr.status_code == 200
        items = rr.json()["items"]
        assert all(i["date"] != "2026-10-21" for i in items), "staff-only draft leaked"
        titles_by_child[p["name"]] = {i["id"] for i in items}
    assert len({frozenset(v) for v in titles_by_child.values()}) > 1
def test_unauth_rejected(client):
    assert client.get("/mobile/calendar", params={"month": "2026-10"}).status_code == 401
