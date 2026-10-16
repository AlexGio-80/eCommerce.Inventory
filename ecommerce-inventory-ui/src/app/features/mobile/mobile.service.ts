import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../../core/models/api-response';
import { Blueprint } from '../../core/models/blueprint';

export interface CardReading {
  name?: string;
  printedName?: string;
  setCode?: string;
  collectorNumber?: string;
  language?: string;
  isFoil: boolean;
  confidence?: string;
  notes?: string;
}

export interface ScanCandidate {
  blueprintId: number;
  cardTraderId: number;
  name: string;
  italianName?: string;
  version?: string;
  expansionName: string;
  expansionCode: string;
  collectorNumber?: string;
  rarity?: string;
  imageUrl?: string;
  exactPrint: boolean;
}

export interface CardScanResult {
  reading: CardReading;
  candidates: ScanCandidate[];
  usedToday: number;
  dailyLimit: number;
}

export interface RulesPrice {
  suggestedPrice?: number;
  outcome: string;
  reason: string;
  comparableOffersCount: number;
  referencePrice?: number;
  profileName?: string;
}

export interface MyListing {
  quantity: number;
  price: number;
  condition: string;
  language: string;
  isFoil: boolean;
}

export interface CardValuation {
  card: ScanCandidate;
  condition: string;
  language: string;
  isFoil: boolean;
  rulesPrice?: RulesPrice;
  ctOffers: number;
  ctLowest?: number;
  ctZeroLowest?: number;
  cmTrend?: number;
  cmLow?: number;
  cmDate?: string;
  myListings: MyListing[];
}

export const CONDITIONS = ['Near Mint', 'Slightly Played', 'Moderately Played', 'Played', 'Poor'];
export const LANGUAGES = ['English', 'Italian', 'Japanese', 'French', 'German', 'Spanish', 'Chinese', 'Portuguese', 'Russian', 'Korean'];

/** Chiamate della sezione per il telefono (feature 006). */
@Injectable({ providedIn: 'root' })
export class MobileService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/api`;

  recognize(photo: Blob): Observable<CardScanResult> {
    const form = new FormData();
    form.append('image', photo, 'carta.jpg');
    return this.http.post<ApiResponse<CardScanResult>>(`${this.apiUrl}/scan/recognize`, form)
      .pipe(map(r => r.data!));
  }

  valuate(blueprintId: number, condition: string, language: string, isFoil: boolean): Observable<CardValuation> {
    const params = new HttpParams()
      .set('condition', condition)
      .set('language', language)
      .set('isFoil', isFoil);
    return this.http.get<ApiResponse<CardValuation>>(`${this.apiUrl}/scan/card/${blueprintId}`, { params })
      .pipe(map(r => r.data!));
  }

  /** Stessa ricerca della maschera "Nuovo Prodotto", solo carte Magic. */
  search(text: string): Observable<Blueprint[]> {
    const params = new HttpParams().set('name', text);
    return this.http.get<Blueprint[]>(`${this.apiUrl}/cardtrader/blueprints/search`, { params })
      .pipe(map(list => list.filter(b => !b.game || b.game.name === 'Magic')));
  }
}

/** Errore dell'API in una frase, per la pagina. */
export function errorMessage(err: any, fallback: string): string {
  return err?.error?.message || fallback;
}

/**
 * Rimpicciolisce la foto prima dell'invio (lato lungo al massimo 1.400 px, JPEG): da qualche MB a
 * qualche centinaio di KB, che in 4G fa la differenza, e basta per leggere nome e numero di raccolta.
 */
export async function shrinkPhoto(file: File, maxSide = 1400): Promise<Blob> {
  const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
  const scale = Math.min(1, maxSide / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(bitmap.width * scale);
  canvas.height = Math.round(bitmap.height * scale);
  canvas.getContext('2d')!.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();
  return new Promise((resolve, reject) =>
    canvas.toBlob(b => b ? resolve(b) : reject(new Error('Foto non convertita')), 'image/jpeg', 0.85));
}
