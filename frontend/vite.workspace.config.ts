// Preview-workspace dev server only: the repo's vite.config.ts plus the public preview host.
import { defineConfig, mergeConfig } from 'vite';
import base from './vite.config.ts';

const origin = new URL(process.env.ORIGIN || 'http://localhost:3000');
const secure = origin.protocol === 'https:';

export default mergeConfig(base, defineConfig({
  plugins: [{
    name: 'lefteria-workspace-forwarded-host',
    configureServer(server) {
      // The ingress forwards an internal cluster Host; present the public preview host instead.
      server.middlewares.use((req, _res, next) => {
        if (req.headers['x-forwarded-host'] === origin.host) req.headers.host = origin.host;
        next();
      });
    }
  }],
  server: {
    host: '0.0.0.0',
    port: Number(process.env.PORT || 3000),
    strictPort: true,
    // The ingress forwards an internal cluster Host header (suffix set in scripts/workspace/workspace.conf).
    allowedHosts: [origin.hostname, ...(process.env.LEFTERIA_WEB_EXTRA_ALLOWED_HOSTS || '').split(',').filter(Boolean)],
    hmr: secure ? { protocol: 'wss', host: origin.hostname, clientPort: 443 } : undefined
  }
}));
