import { Geolocation } from '@capacitor/geolocation';
import { isNativeApp } from '@/lib/platform';

export type UserPosition = { lat: number; lng: number };

export function locationSupported(): boolean {
  return isNativeApp() || (typeof navigator !== 'undefined' && 'geolocation' in navigator);
}

/** The current position: the phone's GPS inside the app (native permission dialog), the browser's geolocation on the web. */
export async function currentPosition(): Promise<UserPosition> {
  if (isNativeApp()) {
    const { coords } = await Geolocation.getCurrentPosition({ enableHighAccuracy: true, timeout: 10000 });
    return { lat: coords.latitude, lng: coords.longitude };
  }
  return new Promise<UserPosition>((resolve, reject) => {
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => resolve({ lat: coords.latitude, lng: coords.longitude }),
      reject,
      { enableHighAccuracy: true, timeout: 10000 },
    );
  });
}
