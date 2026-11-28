import importlib.util
import pathlib
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("preview_baseline", pathlib.Path(__file__).with_name("preview-baseline.py"))
baseline = importlib.util.module_from_spec(spec)
spec.loader.exec_module(baseline)

class PreviewBaselineTests(unittest.TestCase):
    def test_exact_preview_only(self):
        baseline.validate_target(baseline.PREVIEW, "a" * 40)
        for target in ("https://lefteriafc.cy", "https://www.lefteriafc.cy",
                       baseline.PREVIEW + "/", baseline.PREVIEW + "@other.test",
                       "http://srv1649602.hstgr.cloud:8443", "https://srv1649602.hstgr.cloud"):
            with self.assertRaises(ValueError):
                baseline.validate_target(target, "a" * 40)
        with self.assertRaises(ValueError):
            baseline.validate_target(baseline.PREVIEW, "main")

    def test_crawler_and_cache_guard(self):
        headers = {"Content-Type": "text/html", "X-Robots-Tag": "noindex, nofollow",
                   "Cache-Control": "no-store"}
        self.assertTrue(baseline.validate_payload("html", None, headers, b"<html>"))
        for key in ("X-Robots-Tag", "Cache-Control"):
            self.assertFalse(baseline.validate_payload("html", None,
                             {k: v for k, v in headers.items() if k != key}, b"<html>"))

    def test_validated_public_news(self):
        headers = {"Content-Type": "application/json", "X-Robots-Tag": "noindex",
                   "Cache-Control": "no-store"}
        self.assertTrue(baseline.validate_payload("news", None, headers, b'{"total":1,"items":[{}]}'))
        self.assertFalse(baseline.validate_payload("news", None, headers, b'{"total":2,"items":[{}]}'))
        self.assertFalse(baseline.validate_payload("site", "lefteria_fc", headers,
                         b'{"organization_id":"lefteria_fc_academy"}'))

    def test_nearest_rank_p95(self):
        self.assertEqual(baseline.p95([10, 30, 20]), 30)
        self.assertIsNone(baseline.p95([]))

    @patch.object(baseline, "probe", return_value={"valid": False, "duration_ms": 10})
    @patch("builtins.print")
    def test_first_failure_aborts(self, print_mock, probe_mock):
        self.assertEqual(baseline.run(baseline.PREVIEW, "a" * 40), 1)
        self.assertEqual(probe_mock.call_count, 1)
        self.assertIn('"aborted": true', print_mock.call_args.args[0])

    @patch.object(baseline.time, "sleep")
    @patch.object(baseline, "probe", return_value={"valid": True, "duration_ms": 10})
    @patch("builtins.print")
    def test_success_is_bounded(self, print_mock, probe_mock, sleep_mock):
        self.assertEqual(baseline.run(baseline.PREVIEW, "a" * 40), 0)
        self.assertEqual(probe_mock.call_count, 3 * len(baseline.ROUTES))
        self.assertIn('"aborted": false', print_mock.call_args.args[0])

if __name__ == "__main__":
    unittest.main()
