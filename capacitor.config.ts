import type { CapacitorConfig } from '@capacitor/cli';

// Phone app (Android and iOS): a native shell around the static export of the web frontend (out/).
// See docs/mobile-app.md and docs/adr/0008-mobile-app-with-capacitor.md.
//
// CAP_SERVER_URL      load the app from a running dev server instead of the bundled files, e.g. http://192.168.1.20:3000
// CAP_ALLOW_HTTP_API  1 = the web view may call an http:// API (development on the local network only)
const devServer = process.env.CAP_SERVER_URL;

const config: CapacitorConfig = {
  appId: 'fun.openquest.app',
  appName: 'OpenQuest',
  webDir: 'out',
  server: {
    // The web view then runs under https://localhost (the origin the API's CORS settings allow).
    androidScheme: 'https',
    ...(devServer ? { url: devServer, cleartext: true } : {}),
  },
  android: { allowMixedContent: process.env.CAP_ALLOW_HTTP_API === '1' },
  ios: { contentInset: 'always' },
};

export default config;
