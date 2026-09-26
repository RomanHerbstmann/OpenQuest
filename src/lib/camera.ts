import { Camera, CameraDirection, CameraResultType, CameraSource } from '@capacitor/camera';

/** The person closed the camera or the picker without choosing a photo. Nothing to report. */
export class PhotoCancelled extends Error {
  constructor() {
    super('cancelled');
    this.name = 'PhotoCancelled';
  }
}

/** The person did not allow access to the camera or the photos. */
export class PhotoPermissionDenied extends Error {
  constructor() {
    super('denied');
    this.name = 'PhotoPermissionDenied';
  }
}

// Longest side of the photo in pixels. A JPEG of that size is 1 to 2 MB; the API accepts up to 10 MB and does not need more.
const MAX_SIDE = 2048;

async function getPhoto(source: CameraSource): Promise<Blob> {
  try {
    const photo = await Camera.getPhoto({
      source,
      resultType: CameraResultType.Uri,
      quality: 85,
      width: MAX_SIDE,
      height: MAX_SIDE,
      correctOrientation: true,
      allowEditing: false,
      saveToGallery: false,
      direction: CameraDirection.Rear,
    });
    if (!photo.webPath) throw new Error('Das Foto konnte nicht gelesen werden.');
    const blob = await (await fetch(photo.webPath)).blob();
    return blob.type ? blob : new Blob([blob], { type: 'image/jpeg' });
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    if (/cancel/i.test(message)) throw new PhotoCancelled();
    if (/denied|permission/i.test(message)) throw new PhotoPermissionDenied();
    throw error;
  }
}

/** Opens the phone's own camera app (native, only inside the phone app) and returns the photo as a JPEG. */
export const takePhoto = () => getPhoto(CameraSource.Camera);

/** Opens the photo library and returns the chosen photo. */
export const pickPhoto = () => getPhoto(CameraSource.Photos);
