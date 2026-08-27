import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject } from 'rxjs';
import { tap } from 'rxjs/operators';
import { environment } from '../../../environments/environment';

export interface LoginDto {
    username: string;
    password: string;
}

export interface AuthResponse {
    token: string;
    username: string;
    role: string;
    expiresIn: number;
}

@Injectable({
    providedIn: 'root'
})
export class AuthService {
    private apiUrl = `${environment.apiUrl}/api/auth`;
    private tokenKey = 'auth_token';
    private currentUserSubject = new BehaviorSubject<AuthResponse | null>(null);
    public currentUser$ = this.currentUserSubject.asObservable();

    constructor(private http: HttpClient) {
        this.loadTokenFromStorage();
    }

    private loadTokenFromStorage(): void {
        const token = localStorage.getItem(this.tokenKey);
        if (token) {
            // Optionally decode and validate token here
            // For now, just set it as logged in
            const storedUser = localStorage.getItem('current_user');
            if (storedUser) {
                this.currentUserSubject.next(JSON.parse(storedUser));
            }
        }
    }

    login(username: string, password: string): Observable<any> {
        return this.http.post<any>(`${this.apiUrl}/login`, { username, password }).pipe(
            tap(response => {
                if (response.success && response.data) {
                    localStorage.setItem(this.tokenKey, response.data.token);
                    localStorage.setItem('current_user', JSON.stringify(response.data));
                    this.currentUserSubject.next(response.data);
                }
            })
        );
    }

    logout(): void {
        localStorage.removeItem(this.tokenKey);
        localStorage.removeItem('current_user');
        this.currentUserSubject.next(null);
    }

    getToken(): string | null {
        return localStorage.getItem(this.tokenKey);
    }

    /**
     * Un token presente ma scaduto non è una sessione valida: prima questo metodo
     * controllava solo l'esistenza della stringa, così l'AuthGuard lasciava navigare
     * mentre ogni chiamata API rispondeva 401 e le pagine restavano vuote senza
     * spiegazione. La scadenza si legge dal claim `exp` del JWT.
     */
    isAuthenticated(): boolean {
        const token = this.getToken();
        if (!token) return false;

        const expiry = this.getTokenExpiry(token);

        // Token illeggibile: si lascia decidere al server, che risponderà 401 se non va bene.
        if (expiry === null) return true;

        if (expiry <= Date.now()) {
            this.logout();
            return false;
        }

        return true;
    }

    /** Millisecondi della scadenza del token, o null se il token non è decodificabile. */
    private getTokenExpiry(token: string): number | null {
        try {
            const payload = token.split('.')[1];
            if (!payload) return null;

            // base64url → base64
            const normalized = payload.replace(/-/g, '+').replace(/_/g, '/');
            const decoded = JSON.parse(atob(normalized));

            return typeof decoded.exp === 'number' ? decoded.exp * 1000 : null;
        } catch {
            return null;
        }
    }

    getCurrentUser(): AuthResponse | null {
        return this.currentUserSubject.value;
    }
}
