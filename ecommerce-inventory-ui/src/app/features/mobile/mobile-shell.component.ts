import { Component, OnInit, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MobileService } from './mobile.service';

/** Cornice della sezione per il telefono: barra in alto, pagina, barra di navigazione in basso. */
@Component({
  selector: 'app-mobile-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatIconModule],
  template: `
    <div class="shell">
      <header class="top">
        <span class="title">eCommerce Inventory</span>
        <a routerLink="/layout/dashboard" class="full">Versione completa</a>
      </header>
      <main class="page">
        <router-outlet></router-outlet>
      </main>
      <nav class="bottom">
        <a routerLink="scan" routerLinkActive="active">
          <mat-icon>photo_camera</mat-icon><span>Scansiona</span>
        </a>
        <a routerLink="search" routerLinkActive="active">
          <mat-icon>search</mat-icon><span>Cerca</span>
        </a>
        <a routerLink="queue" routerLinkActive="active">
          <span class="icon"><mat-icon>playlist_add_check</mat-icon>
            @if (mobile.queueCount()) { <span class="count">{{ mobile.queueCount() }}</span> }
          </span><span>Coda</span>
        </a>
      </nav>
    </div>
  `,
  styles: [`
    .shell { display: flex; flex-direction: column; height: 100dvh; background: #fafafa; }
    .top { display: flex; align-items: center; justify-content: space-between; padding: 10px 14px;
           background: #3f51b5; color: #fff; }
    .title { font-weight: 500; font-size: 16px; }
    .full { color: #c5cae9; font-size: 13px; text-decoration: none; }
    .page { flex: 1; overflow-y: auto; padding: 12px; }
    .bottom { display: flex; border-top: 1px solid #ddd; background: #fff; }
    .bottom a { flex: 1; display: flex; flex-direction: column; align-items: center; padding: 6px 0 8px;
                color: #666; text-decoration: none; font-size: 12px; }
    .bottom a.active { color: #3f51b5; }
    .icon { position: relative; display: inline-flex; }
    .count { position: absolute; top: -4px; right: -12px; min-width: 16px; height: 16px; padding: 0 4px; box-sizing: border-box;
             border-radius: 8px; background: #e53935; color: #fff; font-size: 10px; line-height: 16px; text-align: center; }
  `]
})
export class MobileShellComponent implements OnInit {
  mobile = inject(MobileService);

  ngOnInit(): void {
    this.mobile.refreshQueueCount().subscribe();
  }
}
