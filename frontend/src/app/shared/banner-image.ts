/** Longest edge of an uploaded banner; anything bigger is scaled down in the browser. */
export const MAX_BANNER_EDGE = 1600;
const QUALITY = 0.85;

/** Scales (width, height) down to fit within `max` on both edges, keeping the aspect ratio. */
export function fitWithin(width: number, height: number, max: number): [number, number] {
  const scale = Math.min(1, max / Math.max(width, height));
  return [Math.max(1, Math.round(width * scale)), Math.max(1, Math.round(height * scale))];
}

/**
 * Decodes an image the user picked and re-encodes it as a JPEG no larger than
 * {@link MAX_BANNER_EDGE}, which keeps uploads small and strips metadata such as GPS tags.
 * Rejects if the file can't be decoded as an image.
 */
export async function prepareBanner(file: File): Promise<Blob> {
  const bitmap = await createImageBitmap(file);
  try {
    const [width, height] = fitWithin(bitmap.width, bitmap.height, MAX_BANNER_EDGE);
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');
    if (!ctx) {
      throw new Error('Canvas is not available');
    }
    // JPEG has no alpha channel; transparent PNGs would otherwise turn black.
    ctx.fillStyle = '#fff';
    ctx.fillRect(0, 0, width, height);
    ctx.drawImage(bitmap, 0, 0, width, height);
    return await new Promise<Blob>((resolve, reject) =>
      canvas.toBlob(
        (blob) => (blob ? resolve(blob) : reject(new Error('Could not encode image'))),
        'image/jpeg',
        QUALITY,
      ),
    );
  } finally {
    bitmap.close();
  }
}
