"""Pure negative tests; never opens Docker or a database connection."""
import importlib.util
from pathlib import Path
import unittest

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("drill", HERE / "disposable-postgres-restore-drill.py")
drill = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(drill)

EXPECTED_ENV = {
    "GITHUB_ACTIONS": "true",
    "GITHUB_REPOSITORY": "MikeCy-com/Lefteria-FC-OS",
    "LEFTERIA_RESTORE_DRILL_DISPOSABLE_ONLY": "1",
    "POSTGRES_DB": "lefteria_rewrite_identity_tests",
    "POSTGRES_USER": "lefteria_tests",
}
EXPECTED_FIXTURE = {
    "disposable": True,
    "purpose": "Lefteria rewrite identity integration tests only",
    "host": "127.0.0.1",
    "port": 54329,
    "database": "lefteria_rewrite_identity_tests",
    "username": "lefteria_tests",
}


class RestoreGuardTests(unittest.TestCase):
    def test_exact_isolated_fixture_is_the_only_allowlisted_source(self):
        self.assertEqual(drill.validate_environment(EXPECTED_ENV, EXPECTED_FIXTURE),
                         "lefteria_tests")

    def test_any_production_or_staging_fixture_is_refused(self):
        for field, malicious in [
            ("disposable", False),
            ("purpose", "Lefteria production backup"),
            ("host", "srv1649602.hstgr.cloud"),
            ("port", 5432),
            ("database", "lefteria_live"),
            ("username", "live_owner"),
        ]:
            with self.subTest(field=field):
                alternate = {**EXPECTED_FIXTURE, field: malicious}
                with self.assertRaises(ValueError):
                    drill.validate_environment(EXPECTED_ENV, alternate)

    def test_non_ci_and_target_changes_are_refused(self):
        for field, value in [
            ("GITHUB_ACTIONS", "false"),
            ("GITHUB_REPOSITORY", "MikeCy-com/Lefteria-FC"),
            ("LEFTERIA_RESTORE_DRILL_DISPOSABLE_ONLY", "0"),
            ("POSTGRES_DB", "lefteria_production"),
            ("POSTGRES_USER", "system_owner"),
        ]:
            with self.subTest(field=field):
                with self.assertRaises(ValueError):
                    drill.validate_environment({**EXPECTED_ENV, field: value},
                                               EXPECTED_FIXTURE)

    def test_refuses_invalid_account_identifier_even_if_fixture_matches(self):
        env = {**EXPECTED_ENV, "POSTGRES_USER": "owner; DROP DATABASE live"}
        fixture = {**EXPECTED_FIXTURE, "username": env["POSTGRES_USER"]}
        with self.assertRaises(ValueError):
            drill.validate_environment(env, fixture)

    def test_catalog_fingerprints_aggregate_sorted_content_not_private_rows(self):
        sql = drill.fingerprint_query([
            ("public", "ci_private_123"), ("identity", "mobile_identities")])
        self.assertIn("count(*)::bigint", sql)
        self.assertIn("row_to_json(t)::text", sql)
        self.assertIn('FROM "identity"."mobile_identities" AS t', sql)
        self.assertIn("ORDER BY relation", sql)
        self.assertNotIn("SELECT * FROM identity.mobile_identities", sql)
        with self.assertRaises(ValueError):
            drill.fingerprint_query([])
        with self.assertRaises(ValueError):
            drill.fingerprint_query([("public", "duplicate"), ("public", "duplicate")])
        with self.assertRaises(ValueError):
            drill.fingerprint_query([("pg_catalog", "pg_authid")])


if __name__ == "__main__":
    unittest.main()
