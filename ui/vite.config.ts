import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

const DAEMON_ADDRESS = '127.0.0.1:5757';
const daemonOrigin = `http://${DAEMON_ADDRESS}`;

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  define: {
    __DAEMON_ADDRESS__: JSON.stringify(DAEMON_ADDRESS),
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/hubs': { target: daemonOrigin, ws: true },
      '/api': { target: daemonOrigin },
    },
  },
  build: {
    // The daemon serves the built UI in production.
    outDir: '../src/Host.Daemon/wwwroot',
    emptyOutDir: true,
  },
});
