# OpenQuest as a phone app (Android and iOS)

The phone app is the web frontend in a native shell ([Capacitor](https://capacitorjs.com), decision in
[ADR-0008](adr/0008-mobile-app-with-capacitor.md)). Inside the app the scan dialog opens the **phone's own camera** and the map uses the
**phone's GPS**; in a browser nothing changes.

| What | Where |
|---|---|
| Shell configuration | `capacitor.config.ts` (app id `fun.openquest.app`) |
| Static export for the app | `next.config.ts` + `scripts/mobile-build.mjs` (`MOBILE_BUILD=1` → `out/`) |
| Native camera | `src/lib/camera.ts`, used by `src/components/scan/ScanModal.tsx` |
| Native GPS | `src/lib/position.ts`, used by `src/components/map/MapExplorer.tsx` |
| "Am I in the app?" | `src/lib/platform.ts` (`isNativeApp()`) |
| API address | `src/lib/config.ts` (`NEXT_PUBLIC_API_URL`) |
| Permissions for the native projects | `scripts/mobile-native-setup.mjs` |

## What you need

- Node 20.9+ and pnpm (as for the web frontend).
- **Android:** JDK 17 (Android Gradle Plugin 8.7, Gradle 8.11), the Android SDK with platform **android-35** (`sdkmanager "platforms;android-35"`; Gradle usually
  fetches missing parts itself once the licenses are accepted) and `ANDROID_HOME` set. A phone with developer options and USB debugging on (or an emulator).
  The project compiles against Android 15 (API 35) and runs on Android 6+ (minSdk 23).
- **iOS:** a Mac with Xcode 16+ and CocoaPods; an Apple ID for running on your own phone (a paid developer account only for TestFlight/App Store).

## Setup

The native projects are already in the repository (`android/` and `ios/`, app id `fun.openquest.app`, permissions for camera, photos and location included;
Capacitor's own `.gitignore` keeps build output and the copied web files out of git).

```bash
pnpm install                 # installs Capacitor
cp .env.example .env.local   # set NEXT_PUBLIC_API_URL (see "The API" below)
```

`pnpm mobile:init` (export, `cap add android`, `cap add ios`, permissions, sync) is only needed to recreate the native projects from scratch, for example after
deleting them or changing the app id in `capacitor.config.ts`.

## Status

Checked in a throw-away container: `pnpm install --frozen-lockfile`, `tsc`, the web build, the static export (`MOBILE_BUILD=1`), `cap add android` / `cap add ios`,
`cap sync` and the permission script (twice, to check that it is idempotent). **Not yet run on a phone or an emulator**: no Android SDK build and no Xcode build
was done, so expect small fixes on the first real run.

## Run on an Android phone

```bash
adb devices                  # the phone must be listed (USB debugging allowed)
pnpm mobile:android          # export, sync, build, install and start on the phone
```

Or build an APK you can hand around:

```bash
pnpm mobile:apk
adb install -r android/app/build/outputs/apk/debug/app-debug.apk    # or copy the file to the phone and open it
```

The first start asks for the camera and location permission. (Installing an APK from outside the Play Store needs "install unknown apps" allowed on the phone.)
A store release needs a signed release build (`./gradlew bundleRelease` with your own keystore); that is not set up here.

## Run on an iPhone

```bash
pnpm mobile:build            # export and sync into ios/
pnpm mobile:ios:open         # opens Xcode
```

In Xcode choose the `App` target, set your **Team** under *Signing & Capabilities*, pick your phone and press Run. The first launch asks for camera and location.

## The API

The scan flow (`POST /api/verify`, photo verification) and the Jev search (`POST /api/tree-search`) are **Next.js server routes** of the web app, not part of the
static export. `pnpm mobile:build` therefore sets `src/app/api` aside while it exports (and puts it back afterwards), and the app calls both routes on the deployed
web app. The app has no origin of its own, so the full address goes into `NEXT_PUBLIC_API_URL` at build time (for example `https://openquest.fun`); without it the
scan and the search fail. The web view runs under `https://localhost` (Android) and `capacitor://localhost` (iOS); the routes answer these cross-origin requests
(CORS headers in `next.config.ts`).

- **Best:** the deployed web app over HTTPS, or a tunnel to your laptop's `pnpm dev` such as `cloudflared tunnel --url http://localhost:3000`. Put the address into
  `NEXT_PUBLIC_API_URL` and rebuild (`pnpm mobile:build`).
- **Development on the same Wi-Fi:** run `pnpm dev` (listens on all interfaces), set `NEXT_PUBLIC_API_URL=http://<your computer's address>:3000` and build with
  `CAP_ALLOW_HTTP_API=1 pnpm mobile:build`. Android then allows plain http from the https web view; on iOS local-network http is allowed by the setup script.
  Never ship this setting.
- Photos are sent as JPEG (longest side 1280 px after downscaling in `src/lib/treeScan.ts`; the native camera hands over up to 2048 px).
- The quests, claims and login go to the **.NET API** (`src/lib/api.ts`). The web app reaches it through the `/backend` proxy (rewrite in `next.config.ts`); the static
  export has no proxy, so the app calls the API directly at `NEXT_PUBLIC_BACKEND_URL` (`pnpm mobile:build` defaults it to `OPENQUEST_API_URL`, else
  `https://openquest-api.onrender.com`). That API must allow the web view's origins in CORS: `Cors__Origins` has to contain `https://localhost` and
  `capacitor://localhost` (the Development settings already do; check the deployed instance).

## Working on the frontend with live reload

```bash
pnpm dev                                          # on your computer, port 3000, listens on all interfaces
CAP_SERVER_URL=http://<your computer's address>:3000 pnpm cap sync
pnpm cap run android                              # the app loads the page from your dev server; edits show up on save
```

Unset `CAP_SERVER_URL` and run `pnpm mobile:build` again to go back to the bundled files. Live reload needs the phone and the computer in the same network.

## Good to know

- The app is a static export: no API routes, server actions or image optimization in the frontend (dynamic routes need `generateStaticParams`); `src/app/api` is left out of the export, see "The API".
- The web build (`pnpm build`) is unchanged; `MOBILE_BUILD=1` only switches on the export.
- After changing the web code, run `pnpm mobile:build` (or `pnpm mobile:android`) again: the app contains a copy of `out/`.
- Icon and splash screen are still the Capacitor defaults. Put a 1024 px `resources/icon.png` (and `resources/splash.png`, 2732 px) into the repository and run
  `pnpm dlx @capacitor/assets generate` to create all sizes.
- Camera and photo library permissions can be changed later in the phone's settings; if they were refused the scan dialog says so.

## Troubleshooting

| Problem | Try |
|---|---|
| `adb devices` shows the phone as *unauthorized* | Accept the "Allow USB debugging" dialog on the phone; `adb kill-server && adb start-server`. |
| The app opens but shows a blank page | Run `pnpm mobile:build` (the app needs `out/`); with `CAP_SERVER_URL` set the dev server must be reachable from the phone. |
| Requests fail with a network or CORS error | `NEXT_PUBLIC_API_URL` wrong or unreachable from the phone; see "The API" (CORS headers, http on the local network). |
| Camera button does nothing | Permission refused: enable *Camera* for OpenQuest in the phone's settings. Run `pnpm mobile:setup` if the manifest / Info.plist entries are missing. |
| Location "not available" | Location services off, or permission refused (choose *precise*, *while using the app*). |
| Gradle or Xcode errors after updating Capacitor | `pnpm cap sync`; open the project once in Android Studio / Xcode and let it upgrade the tooling. |
