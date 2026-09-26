export type ScanResult = {
  species: string;
  source: 'demo';
};

// Austauschpunkt für die spätere Backend-Bilderkennung.
export async function recognizeTreeForPrototype(image: Blob, expectedSpecies?: string): Promise<ScanResult> {
  if (!image.type.startsWith('image/') || image.size === 0) {
    throw new Error('Bitte verwende ein gültiges Bild.');
  }

  await new Promise((resolve) => window.setTimeout(resolve, 1800));
  return { species: expectedSpecies ?? 'Birke', source: 'demo' };
}
