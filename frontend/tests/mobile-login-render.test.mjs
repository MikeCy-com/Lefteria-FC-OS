import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { fileURLToPath } from 'node:url';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';

test('rendered OTP pattern accepts exactly six ASCII digits', async () => {
  const hooks = registerHooks({
    load(url, context, nextLoad) {
      if (url.endsWith('.svelte')) {
        const filename = fileURLToPath(url);
        const { js } = compile(readFileSync(filename, 'utf8'), { filename, generate: 'server' });
        return { format: 'module', source: js.code, shortCircuit: true };
      }
      if (url.endsWith('.css')) {
        return { format: 'module', source: 'export {};', shortCircuit: true };
      }
      return nextLoad(url, context);
    }
  });
  try {
    const { default: MobileLogin } = await import('../src/lib/mobile/components/MobileLogin.svelte');
    const noop = () => {};
    const { body } = render(MobileLogin, { props: {
      step: 'otp', phone: '+35799000000', onRequestOtp: noop, onVerifyOtp: noop,
      onChooseRole: noop, onRegisterFan: noop, onStepChange: noop
    } });
    const otpInput = body.match(/<input\b[^>]*\bdata-testid="otp-input"[^>]*>/)?.[0];
    assert.ok(otpInput, 'The actual OTP input must be rendered.');
    const pattern = otpInput.match(/\bpattern="([^"]*)"/)?.[1];
    assert.ok(pattern, 'The rendered OTP input must declare a validation pattern.');
    const browserPattern = new RegExp(`^(?:${pattern})$`, 'v');
    for (const value of ['000000', '123456', '999999']) {
      assert.equal(browserPattern.test(value), true, `Six ASCII digits must be accepted: ${value}`);
    }
    for (const value of ['', '12345', '1234567', 'abcdef', '12345a', '١٢٣٤٥٦', '１２３４５６']) {
      assert.equal(browserPattern.test(value), false, `Invalid OTP must be rejected: ${value}`);
    }
  } finally {
    hooks.deregister();
  }
});
