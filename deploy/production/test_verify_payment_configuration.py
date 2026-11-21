"""Pure tests for sandbox payment/SMS release guards (no provider calls)."""
import importlib.util
from pathlib import Path
import unittest

path = Path(__file__).resolve().with_name("verify-compose-security.py")
spec = importlib.util.spec_from_file_location("compose_guards", path)
guards = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guards)

KEYS = {
    "Payabl__Enabled": "false",
    "Payabl__Sandbox": "true",
    "Payabl__MerchantId": "",
    "Payabl__Secret": "",
    "Payabl__PublicOrigin": "",
    "TwilioPaymentSms__Enabled": "false",
    "TwilioPaymentSms__AccountSid": "",
    "TwilioPaymentSms__AuthToken": "",
    "TwilioPaymentSms__MessagingServiceSid": "",
}


def services(api_overrides=None, public_origin="https://academy.example.invalid"):
    return {
        "api": {"environment": {**KEYS, **(api_overrides or {})}},
        "web": {"environment": {"ORIGIN": public_origin}},
    }


class PaymentConfigGuards(unittest.TestCase):
    def test_disabled_by_default_allows_no_provider_configuration(self):
        guards.verify_provider_wiring(services())

    def test_all_settings_must_be_explicitly_forwarded(self):
        s = services()
        del s["api"]["environment"]["Payabl__Secret"]
        with self.assertRaises(SystemExit):
            guards.verify_provider_wiring(s)

    def test_sandbox_only_merchant_can_be_manually_configured(self):
        guards.verify_provider_wiring(services({
            "Payabl__Enabled": "true",
            "Payabl__Sandbox": "true",
            "Payabl__MerchantId": "sandboxMerchant7",
            "Payabl__Secret": "synthetic_only_secret_123456",
            "Payabl__PublicOrigin": "https://academy.example.invalid",
        }))

    def test_production_payabl_is_rejected_even_when_disabled(self):
        with self.assertRaises(SystemExit):
            guards.verify_provider_wiring(services({"Payabl__Sandbox": "false"}))

    def test_unconfigured_or_wrong_origin_sandbox_is_denied(self):
        on = {"Payabl__Enabled": "true",
              "Payabl__MerchantId": "sandboxMerchant7",
              "Payabl__Secret": "synthetic_only_secret_123456"}
        for public_origin in ("", "http://academy.example.invalid",
                              "https://other.example.invalid",
                              "https://academy.example.invalid/path",
                              "https://user:password@academy.example.invalid"):
            with self.subTest(public_origin=public_origin):
                with self.assertRaises(SystemExit):
                    guards.verify_provider_wiring(services({
                        **on, "Payabl__PublicOrigin": public_origin}))

    def test_sms_cannot_be_enabled_without_sandbox_payment_gateway(self):
        with self.assertRaises(SystemExit):
            guards.verify_provider_wiring(services({
                "TwilioPaymentSms__Enabled": "true"}))

    def test_sms_requires_own_provider_settings(self):
        payment = {
            "Payabl__Enabled": "true",
            "Payabl__MerchantId": "sandboxMerchant7",
            "Payabl__Secret": "synthetic_only_secret_123456",
            "Payabl__PublicOrigin": "https://academy.example.invalid",
        }
        with self.assertRaises(SystemExit):
            guards.verify_provider_wiring(services({
                **payment, "TwilioPaymentSms__Enabled": "true"}))
        guards.verify_provider_wiring(services({
            **payment,
            "TwilioPaymentSms__Enabled": "true",
            "TwilioPaymentSms__AccountSid": "AC" + "a" * 32,
            "TwilioPaymentSms__AuthToken": "synthetic_only_twilio_token_123456",
            "TwilioPaymentSms__MessagingServiceSid": "MG" + "b" * 32,
        }))


if __name__ == "__main__":
    unittest.main()
