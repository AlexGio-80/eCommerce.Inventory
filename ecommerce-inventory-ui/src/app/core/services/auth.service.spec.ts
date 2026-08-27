import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { AuthService } from './auth.service';

/**
 * Un token presente ma scaduto lasciava navigare nell'applicazione mentre ogni
 * chiamata API rispondeva 401, con le pagine vuote e nessuna spiegazione.
 */
describe('AuthService.isAuthenticated', () => {
    let service: AuthService;

    /** Costruisce un JWT fittizio con la scadenza indicata (solo il payload conta). */
    function tokenWithExpiry(secondsFromNow: number): string {
        const payload = { sub: 'admin', exp: Math.floor(Date.now() / 1000) + secondsFromNow };
        const encode = (o: object) => btoa(JSON.stringify(o)).replace(/=/g, '');
        return `${encode({ alg: 'HS256', typ: 'JWT' })}.${encode(payload)}.firma`;
    }

    beforeEach(() => {
        TestBed.configureTestingModule({ providers: [provideHttpClient()] });
        localStorage.clear();
        service = TestBed.inject(AuthService);
    });

    afterEach(() => localStorage.clear());

    it('senza token non è autenticato', () => {
        expect(service.isAuthenticated()).toBe(false);
    });

    it('con token valido è autenticato', () => {
        localStorage.setItem('auth_token', tokenWithExpiry(3600));
        expect(service.isAuthenticated()).toBe(true);
    });

    it('con token scaduto non è autenticato', () => {
        localStorage.setItem('auth_token', tokenWithExpiry(-60));
        expect(service.isAuthenticated()).toBe(false);
    });

    it('con token scaduto ripulisce la sessione, così il login riparte pulito', () => {
        localStorage.setItem('auth_token', tokenWithExpiry(-60));
        localStorage.setItem('current_user', '{"username":"admin"}');

        service.isAuthenticated();

        expect(localStorage.getItem('auth_token')).toBeNull();
        expect(localStorage.getItem('current_user')).toBeNull();
    });

    it('con token illeggibile lascia decidere al server', () => {
        // Meglio una chiamata che torna 401 di un logout deciso dal client su un
        // formato che non sa interpretare.
        localStorage.setItem('auth_token', 'non-e-un-jwt');
        expect(service.isAuthenticated()).toBe(true);
    });
});
