import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatBadgeModule } from '@angular/material/badge';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AlertNotification, PurchasingService } from '../services/purchasing.service';

/**
 * Campanella nella barra in alto: avvisi sugli acquisti non letti, visibile da ogni pagina. Si
 * aggiorna all'apertura e ogni 5 minuti: gli avvisi nascono una volta al giorno, non serve di più.
 */
@Component({
  selector: 'app-alert-bell',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatMenuModule, MatBadgeModule, MatTooltipModule],
  template: `
    <button mat-icon-button [matMenuTriggerFor]="menu" (menuOpened)="load()" matTooltip="Avvisi sugli acquisti">
      <mat-icon [matBadge]="unread() || null" matBadgeColor="warn" matBadgeSize="small">notifications</mat-icon>
    </button>
    <mat-menu #menu="matMenu" class="alert-menu" xPosition="before">
      <div class="menu-header" (click)="$event.stopPropagation()">
        <strong>Avvisi</strong>
        <button mat-button *ngIf="unread() > 0" (click)="markAllRead()">Segna tutti come letti</button>
      </div>
      <div class="empty" *ngIf="items().length === 0">Nessun avviso</div>
      <button mat-menu-item *ngFor="let n of items()" (click)="open(n)" class="alert-item" [class.unread]="!n.readAt">
        <div class="title">{{ n.title }}</div>
        <div class="message">{{ n.message }}</div>
        <div class="when">{{ n.createdAt | date:'dd/MM/yyyy HH:mm' }}</div>
      </button>
      <button mat-menu-item (click)="openAll()"><mat-icon>list</mat-icon>Tutti gli avvisi e le regole</button>
    </mat-menu>
  `,
  styles: [`
    .menu-header { display: flex; align-items: center; justify-content: space-between; padding: 4px 16px; gap: 12px; }
    .empty { padding: 8px 16px; color: #757575; }
    ::ng-deep .alert-menu { max-width: 460px !important; }
    ::ng-deep .alert-item.mat-mdc-menu-item { height: auto; min-height: 64px; padding-top: 6px; padding-bottom: 6px; }
    ::ng-deep .alert-item .mat-mdc-menu-item-text { white-space: normal; }
    .title { font-weight: 500; }
    .alert-item.unread .title { font-weight: 700; }
    .message { font-size: 12px; color: #616161; white-space: pre-line; max-height: 7.5em; overflow: hidden; }
    .when { font-size: 11px; color: #9e9e9e; }
  `]
})
export class AlertBellComponent implements OnInit, OnDestroy {
  unread = signal(0);
  items = signal<AlertNotification[]>([]);
  private timer?: ReturnType<typeof setInterval>;

  constructor(private purchasing: PurchasingService, private router: Router) { }

  ngOnInit() {
    this.load();
    this.timer = setInterval(() => this.load(), 5 * 60 * 1000);
  }

  ngOnDestroy() {
    if (this.timer) clearInterval(this.timer);
  }

  load() {
    this.purchasing.getNotifications(10).subscribe({
      next: list => { this.unread.set(list.unread); this.items.set(list.items); },
      error: () => { /* la campanella non deve disturbare: l'errore si vede nella scheda Avvisi */ }
    });
  }

  open(notification: AlertNotification) {
    if (!notification.readAt) this.purchasing.markNotificationsRead(notification.id).subscribe({ next: () => this.load() });
    this.router.navigate(['/layout/purchasing'], { queryParams: notification.setCode ? { set: notification.setCode } : {} });
  }

  openAll() {
    this.router.navigate(['/layout/purchasing'], { queryParams: { tab: 'avvisi' } });
  }

  markAllRead() {
    this.purchasing.markNotificationsRead().subscribe({ next: () => this.load() });
  }
}
