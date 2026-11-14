export type InstallPromptEvent = Event & {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
};
export type InstallState = { installed: boolean; available: boolean; busy: boolean; ios: boolean; android: boolean };
export function isAppleMobile(userAgent: string, platform: string, touchPoints: number): boolean {
  return /iPad|iPhone|iPod/.test(userAgent) || platform === 'MacIntel' && touchPoints > 1;
}

// One owner for the browser's single-use prompt, including client-side navigation.
export function createInstallController() {
  let state: InstallState = { installed: false, available: false, busy: false, ios: false, android: false };
  let prompt: InstallPromptEvent | null = null;
  const listeners = new Set<(value: InstallState) => void>();
  function update(patch: Partial<InstallState>) {
    state = { ...state, ...patch };
    for (const listener of listeners) listener(state);
  }
  return {
    subscribe(listener: (value: InstallState) => void) {
      listeners.add(listener); listener(state);
      return () => { listeners.delete(listener); };
    },
    start(browser: Window) {
      const display = browser.matchMedia('(display-mode: standalone)');
      const nav = browser.navigator as Navigator & { standalone?: boolean };
      update({ installed: display.matches || nav.standalone === true,
        ios: isAppleMobile(nav.userAgent, nav.platform, nav.maxTouchPoints),
        android: /Android/i.test(nav.userAgent) });
      const capture = (event: Event) => {
        if (!('prompt' in event) || typeof event.prompt !== 'function') return;
        event.preventDefault(); prompt = event as InstallPromptEvent;
        update({ available: true });
      };
      const installed = () => { prompt = null; update({ installed: true, available: false, busy: false }); };
      const displayChanged = () => update({ installed: display.matches || nav.standalone === true });
      browser.addEventListener('beforeinstallprompt', capture);
      browser.addEventListener('appinstalled', installed);
      display.addEventListener('change', displayChanged);
      // This worker never caches private pages or API responses. Push keeps its
      // existing /mobile/ registration and requires its own explicit opt-in.
      if (browser.isSecureContext && 'serviceWorker' in nav) {
        void nav.serviceWorker.register('/app-service-worker.js', { scope: '/', updateViaCache: 'none' }).catch(() => {});
      }
      return () => {
        browser.removeEventListener('beforeinstallprompt', capture);
        browser.removeEventListener('appinstalled', installed);
        display.removeEventListener('change', displayChanged);
        prompt = null; update({ available: false, busy: false });
      };
    },
    async install(): Promise<'installed' | 'prompted' | 'help' | 'busy'> {
      if (state.installed) return 'installed';
      if (state.busy) return 'busy';
      if (!prompt) return 'help';
      const current = prompt;
      prompt = null; update({ available: false, busy: true });
      try {
        // No await before prompt(): retain the original button's user gesture.
        await current.prompt();
        await current.userChoice;
        return 'prompted';
      } catch {
        return 'help';
      } finally {
        update({ busy: false });
      }
    }
  };
}
export const appInstall = createInstallController();
