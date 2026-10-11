import { GeoPoint } from './models';

/**
 * Field photos are reduced on the device before uploading or queuing them offline: phones take 12 MP
 * images and the evidence only needs to be legible in the ticket PDF.
 */
export async function compressImage(file: Blob, maxSide = 1600, quality = 0.8): Promise<Blob> {
  if (!file.type.startsWith('image/') || file.type === 'image/gif') {
    return file;
  }

  try {
    const bitmap = await createImageBitmap(file);
    const scale = Math.min(1, maxSide / Math.max(bitmap.width, bitmap.height));
    const width = Math.round(bitmap.width * scale);
    const height = Math.round(bitmap.height * scale);
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    canvas.getContext('2d')!.drawImage(bitmap, 0, 0, width, height);
    bitmap.close();
    const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', quality));
    return blob && blob.size < file.size ? blob : file;
  } catch {
    return file;
  }
}

/** Current position of the device (null when denied, unavailable or slow). */
export function currentPosition(timeoutMs = 8000): Promise<GeoPoint | null> {
  if (!('geolocation' in navigator)) {
    return Promise.resolve(null);
  }

  return new Promise((resolve) => {
    navigator.geolocation.getCurrentPosition(
      (position) =>
        resolve({
          latitude: Math.round(position.coords.latitude * 1e6) / 1e6,
          longitude: Math.round(position.coords.longitude * 1e6) / 1e6,
          accuracyMeters: Math.round(position.coords.accuracy),
        }),
      () => resolve(null),
      { enableHighAccuracy: true, timeout: timeoutMs, maximumAge: 60_000 },
    );
  });
}

export function newGuid(): string {
  return crypto.randomUUID();
}
