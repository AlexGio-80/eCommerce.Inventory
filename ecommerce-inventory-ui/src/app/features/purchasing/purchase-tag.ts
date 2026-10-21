/** Prodotto del catalogo quanto basta per proporre il Tag. */
export interface TaggableProduct {
  name: string;
  setCode: string;
  category?: string;
  subtype?: string;
}

/**
 * Tag delle inserzioni di un acquisto, CODICE_TIPO_AAAAMMGG: tipo PB/CB per i box Play/Collector, altrimenti
 * dalla categoria; per i Secret Lair SLD_codice del drop. `date` in formato AAAA-MM-GG.
 */
export function suggestPurchaseTag(product: TaggableProduct, date: string): string {
  const day = date.replace(/-/g, '');
  if (product.subtype?.startsWith('secret_lair')) {
    return `#SLD_${secretLairCode(product.name)}_${day}`;
  }
  const type = product.subtype === 'play' ? 'PB'
    : product.subtype === 'collector' ? 'CB'
    : (product.subtype || product.category || 'X').slice(0, 3).toUpperCase();
  return `#${product.setCode}_${type}_${day}`;
}

/**
 * Codice breve di un drop Secret Lair: iniziali delle parole del nome (al massimo 5), più F se
 * foil. "Secret Lair x Marvel Earth's Mightiest Pets Foil Edition" → MEMPF. Un tag per drop tiene
 * separato il bilancio di ciascuno nella scheda Secret Lair.
 */
function secretLairCode(name: string): string {
  const foil = /\b(foil|etched)\b/i.test(name);
  const words = name
    .replace(/^Secret Lair( Drop)?( x)?\s*/i, '')
    .replace(/\b(Foil|Rainbow|Etched|Edition|Bundle)\b/gi, '')
    .split(/[^A-Za-z0-9]+/)
    .filter(w => w.length > 1 && !/^(the|of|and|an|in|to)$/i.test(w));
  return (words.slice(0, 5).map(w => w[0].toUpperCase()).join('') || 'SL') + (foil ? 'F' : '');
}
