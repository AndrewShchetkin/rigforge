/** Decodes base64 into bytes; returns null for malformed input instead of throwing. */
export function decodeBase64(data: string): Uint8Array | null {
  let binary: string;
  try {
    binary = atob(data);
  } catch {
    return null;
  }
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index++) {
    bytes[index] = binary.charCodeAt(index);
  }
  return bytes;
}

/** CSS color of the `index`-th RGB triplet, or null when the buffer is too short. */
export function rgbTripletAt(bytes: Uint8Array, index: number): string | null {
  const offset = index * 3;
  if (offset + 2 >= bytes.length) {
    return null;
  }
  return `rgb(${bytes[offset] ?? 0},${bytes[offset + 1] ?? 0},${bytes[offset + 2] ?? 0})`;
}
