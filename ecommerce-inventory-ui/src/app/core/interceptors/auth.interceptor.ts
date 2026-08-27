import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
    const authService = inject(AuthService);
    const router = inject(Router);
    const token = authService.getToken();

    if (token) {
        req = req.clone({
            setHeaders: {
                Authorization: `Bearer ${token}`
            }
        });
    }

    return next(req).pipe(
        catchError((error: HttpErrorResponse) => {
            // Sessione rifiutata dal server: senza questo rimando l'utente resta
            // dentro l'applicazione con ogni chiamata che fallisce, davanti a pagine
            // vuote e senza capire che deve semplicemente rifare il login.
            // Il login stesso è escluso: lì un 401 significa credenziali sbagliate.
            const isLoginRequest = req.url.includes('/auth/login');

            if (error.status === 401 && !isLoginRequest) {
                authService.logout();
                router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
            }

            return throwError(() => error);
        })
    );
};
