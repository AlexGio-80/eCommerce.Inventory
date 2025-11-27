import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject } from 'rxjs';
import { tap } from 'rxjs/operators';

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
    private apiUrl = 'http://localhost:5155/api/auth';
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

    isAuthenticated(): boolean {
        return !!this.getToken();
    }

    getCurrentUser(): AuthResponse | null {
        return this.currentUserSubject.value;
    }
}
