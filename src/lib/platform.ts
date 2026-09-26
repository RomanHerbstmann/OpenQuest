import { Capacitor } from '@capacitor/core';

/** True inside the phone app (Android/iOS shell), false in a browser. */
export function isNativeApp(): boolean {
  return typeof window !== 'undefined' && Capacitor.isNativePlatform();
}
