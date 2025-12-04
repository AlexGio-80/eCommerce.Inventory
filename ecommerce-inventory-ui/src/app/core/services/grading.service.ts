import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface GradingResult {
    overallGrade: number;
    centering: number;
    corners: number;
    edges: number;
    surface: number;
    confidence: number;
    provider: string;
    conditionCode: string;    // NM, SP, MP, PL, PO
    conditionName: string;    // Near Mint, Slightly Played, etc.
    imagesAnalyzed: number;
}

@Injectable({
    providedIn: 'root'
})
export class GradingService {
    private apiUrl = `${environment.apiUrl}/api/grading`;

    constructor(private http: HttpClient) { }

    analyzeCard(image: File): Observable<GradingResult> {
        const formData = new FormData();
        formData.append('image', image);
        return this.http.post<GradingResult>(`${this.apiUrl}/analyze`, formData);
    }

    analyzeCardMultiple(images: File[]): Observable<GradingResult> {
        const formData = new FormData();
        images.forEach(image => {
            formData.append('images', image);
        });
        return this.http.post<GradingResult>(`${this.apiUrl}/analyze-multi`, formData);
    }
}
