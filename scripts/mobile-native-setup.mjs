// Adds what the native projects need after `cap add android` / `cap add ios`: permissions for the camera, the photo library and the
// location. Safe to run again: it only adds what is missing. Run from the repository root (pnpm mobile:setup).
import { existsSync, readFileSync, writeFileSync } from 'node:fs';

const androidManifest = 'android/app/src/main/AndroidManifest.xml';
const iosInfoPlist = 'ios/App/App/Info.plist';

const androidEntries = [
  ['android.permission.CAMERA', '<uses-permission android:name="android.permission.CAMERA" />'],
  ['android.permission.ACCESS_FINE_LOCATION', '<uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />'],
  ['android.permission.ACCESS_COARSE_LOCATION', '<uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />'],
  // Not required: the app also runs on devices without them (the photo library and manual browsing still work).
  ['android.hardware.camera', '<uses-feature android:name="android.hardware.camera" android:required="false" />'],
  ['android.hardware.location.gps', '<uses-feature android:name="android.hardware.location.gps" android:required="false" />'],
];

const iosStrings = {
  NSCameraUsageDescription: 'OpenQuest nutzt die Kamera, damit du Bäume fotografieren kannst.',
  NSPhotoLibraryUsageDescription: 'OpenQuest greift auf deine Fotos zu, wenn du ein vorhandenes Baumfoto auswählst.',
  NSLocationWhenInUseUsageDescription: 'OpenQuest nutzt deinen Standort, um Bäume in deiner Nähe zu zeigen und zu prüfen, dass du vor Ort bist.',
};

function patchAndroid() {
  if (!existsSync(androidManifest)) return console.log('android: no project yet (run `pnpm mobile:init` once), skipped');
  let xml = readFileSync(androidManifest, 'utf8');
  const missing = androidEntries.filter(([name]) => !xml.includes(`"${name}"`));
  if (!missing.length) return console.log('android: manifest already complete');
  if (!xml.includes('</manifest>')) throw new Error(`${androidManifest}: no </manifest> found`);
  xml = xml.replace('</manifest>', `${missing.map(([, line]) => `    ${line}\n`).join('')}</manifest>`);
  writeFileSync(androidManifest, xml);
  console.log(`android: added ${missing.map(([name]) => name).join(', ')}`);
}

function patchIos() {
  if (!existsSync(iosInfoPlist)) return console.log('ios: no project yet (run `pnpm mobile:init` once), skipped');
  let plist = readFileSync(iosInfoPlist, 'utf8');
  const additions = [];
  for (const [key, text] of Object.entries(iosStrings)) {
    if (!plist.includes(`<key>${key}</key>`)) additions.push(`\t<key>${key}</key>\n\t<string>${text}</string>\n`);
  }
  // Development against an API on the local network over http (App Transport Security blocks other plain http).
  if (!plist.includes('<key>NSAppTransportSecurity</key>')) {
    additions.push('\t<key>NSAppTransportSecurity</key>\n\t<dict>\n\t\t<key>NSAllowsLocalNetworking</key>\n\t\t<true/>\n\t</dict>\n');
  }
  if (!additions.length) return console.log('ios: Info.plist already complete');
  const end = /<\/dict>\s*<\/plist>\s*$/;
  if (!end.test(plist)) throw new Error(`${iosInfoPlist}: unexpected format`);
  plist = plist.replace(end, `${additions.join('')}</dict>\n</plist>\n`);
  writeFileSync(iosInfoPlist, plist);
  console.log(`ios: added ${additions.length} entr${additions.length === 1 ? 'y' : 'ies'} to Info.plist`);
}

patchAndroid();
patchIos();
