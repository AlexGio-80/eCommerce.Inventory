import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef } from 'ag-grid-community';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import {
  AlertNotification, AlertRule, AlertRuleInput, AlertRuleType, PurchasingService, SealedProductAnalysis
} from '../services/purchasing.service';

interface RuleForm {
  id?: number;
  name: string;
  type: AlertRuleType;
  sealedProductId: number | null;
  onlyThisRelease: boolean;
  category: string;
  threshold: number | null;
  useLowPrice: boolean;
  isActive: boolean;
  sendEmail: boolean;
}

/**
 * Avvisi sugli acquisti (Fase 4): regole e avvisi emessi. Le regole si valutano ogni mattina dopo
 * la classifica delle opportunità; un avviso scatta quando la condizione diventa vera.
 */
@Component({
  selector: 'app-alerts-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, AgGridAngular, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatCheckboxModule, MatTooltipModule, MatProgressSpinnerModule, MatSnackBarModule],
  template: `
    <div class="tab-container">
      <div class="form">
        <div class="form-title">{{ form.id ? 'Modifica regola' : 'Nuova regola' }}
          <span class="muted" *ngIf="setName"> · prodotti di {{ setName }} (cambia uscita nella scheda "Analisi uscita")</span>
        </div>
        <div class="fields">
          <mat-form-field appearance="outline" class="medium">
            <mat-label>Tipo</mat-label>
            <mat-select [(ngModel)]="form.type">
              <mat-option value="PriceBelow">Prezzo sotto soglia</mat-option>
              <mat-option value="PriceDrop">Calo di prezzo in 7 giorni</mat-option>
              <mat-option value="OpeningOpportunity">Apertura conveniente</mat-option>
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline" class="name">
            <mat-label>Nome</mat-label>
            <input matInput [(ngModel)]="form.name" placeholder="es. Play Box Star Trek sotto 130 €">
          </mat-form-field>

          <ng-container *ngIf="form.type !== 'OpeningOpportunity'">
            <mat-form-field appearance="outline" class="product">
              <mat-label>Prodotto</mat-label>
              <mat-select [(ngModel)]="form.sealedProductId">
                <mat-option *ngFor="let p of products" [value]="p.id">{{ p.name }}</mat-option>
              </mat-select>
            </mat-form-field>
          </ng-container>

          <ng-container *ngIf="form.type === 'OpeningOpportunity'">
            <mat-checkbox [(ngModel)]="form.onlyThisRelease" [disabled]="!setCode">Solo {{ setName || 'questa uscita' }}</mat-checkbox>
            <mat-form-field appearance="outline" class="medium">
              <mat-label>Tipo di prodotto</mat-label>
              <mat-select [(ngModel)]="form.category">
                <mat-option value="">Tutti</mat-option>
                <mat-option value="booster_box">Box</mat-option>
                <mat-option value="booster_pack">Busta</mat-option>
                <mat-option value="bundle">Bundle</mat-option>
                <mat-option value="deck">Mazzo</mat-option>
                <mat-option value="box_set">Box set</mat-option>
                <mat-option value="limited_aid_tool">Draft/Prerelease</mat-option>
                <mat-option value="subset">Set di mazzi</mat-option>
              </mat-select>
            </mat-form-field>
          </ng-container>

          <mat-form-field appearance="outline" class="small">
            <mat-label>{{ thresholdLabel() }}</mat-label>
            <input matInput type="number" step="0.01" [(ngModel)]="form.threshold">
          </mat-form-field>
          <mat-checkbox *ngIf="form.type === 'PriceBelow'" [(ngModel)]="form.useLowPrice"
            matTooltip="Confronta il prezzo più basso esposto invece del trend">Usa il prezzo più basso</mat-checkbox>
          <mat-checkbox [(ngModel)]="form.sendEmail">Email</mat-checkbox>
          <mat-checkbox [(ngModel)]="form.isActive">Attiva</mat-checkbox>
        </div>
        <div class="actions">
          <button mat-raised-button color="primary" (click)="save()">{{ form.id ? 'Salva modifiche' : 'Crea regola' }}</button>
          <button mat-button (click)="reset()">Annulla</button>
          <span class="spacer"></span>
          <button mat-stroked-button (click)="evaluate()" [disabled]="isEvaluating()"
            matTooltip="Valuta subito le regole sui dati di oggi (gira comunque da sola ogni mattina)">
            <mat-spinner *ngIf="isEvaluating()" diameter="18"></mat-spinner>
            <mat-icon *ngIf="!isEvaluating()">play_arrow</mat-icon> Valuta ora
          </button>
          <button mat-button (click)="testEmail()" [matTooltip]="emailConfigured() ? 'Invia un\\'email di prova' : 'Invio email non configurato: vedi la sezione Email della configurazione'">
            <mat-icon>{{ emailConfigured() ? 'mark_email_read' : 'unsubscribe' }}</mat-icon> Email di prova
          </button>
        </div>
      </div>

      <div class="grids">
        <div class="grid-block">
          <div class="block-title">Regole</div>
          <ag-grid-angular class="ag-theme-material" [rowData]="rules()" [columnDefs]="ruleColumns" [defaultColDef]="defaultColDef"
            style="width: 100%; height: 100%;"></ag-grid-angular>
        </div>
        <div class="grid-block">
          <div class="block-title">Avvisi emessi
            <button mat-button (click)="markAllRead()" *ngIf="unread() > 0">Segna tutti come letti ({{ unread() }})</button>
          </div>
          <ag-grid-angular class="ag-theme-material" [rowData]="notifications()" [columnDefs]="notificationColumns" [defaultColDef]="defaultColDef"
            (rowClicked)="openNotification($event.data)" style="width: 100%; height: 100%;"></ag-grid-angular>
        </div>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; height: 100%; }
    .tab-container { display: flex; flex-direction: column; gap: 8px; height: 100%; padding: 12px 0; box-sizing: border-box; }
    .form-title, .block-title { font-weight: 600; margin-bottom: 6px; display: flex; align-items: center; gap: 8px; }
    .fields { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; }
    .fields ::ng-deep .mat-mdc-form-field-subscript-wrapper { display: none; }
    .product { width: 380px; } .name { width: 320px; } .medium { width: 210px; } .small { width: 150px; }
    .actions { display: flex; gap: 12px; align-items: center; margin-top: 8px; }
    .spacer { flex: 1; }
    .muted { color: #757575; font-size: 12px; font-weight: normal; }
    .grids { flex: 1; display: flex; flex-direction: column; gap: 12px; min-height: 500px; }
    .grid-block { flex: 1; display: flex; flex-direction: column; min-height: 220px; }
    :host ::ng-deep .ag-theme-material { --ag-header-background-color: #3f51b5; --ag-header-foreground-color: white; }
    :host ::ng-deep .row-action { background: none; border: none; cursor: pointer; color: #3f51b5; padding: 0 4px; }
    :host ::ng-deep .unread { font-weight: 600; }
  `]
})
export class AlertsTabComponent implements OnInit {
  @Input() products: SealedProductAnalysis[] = [];
  @Input() setCode: string | null = null;
  @Input() setName: string | null = null;
  /** Codice dell'uscita da aprire nella scheda di analisi. */
  @Output() openRelease = new EventEmitter<string>();

  rules = signal<AlertRule[]>([]);
  notifications = signal<AlertNotification[]>([]);
  unread = signal(0);
  emailConfigured = signal(false);
  isEvaluating = signal(false);
  form: RuleForm = this.emptyForm();

  private static readonly typeLabels: Record<AlertRuleType, string> = {
    PriceBelow: 'Prezzo sotto soglia', PriceDrop: 'Calo di prezzo', OpeningOpportunity: 'Apertura conveniente'
  };

  ruleColumns: ColDef<AlertRule>[] = [
    { headerName: 'Nome', field: 'name', width: 260 },
    { headerName: 'Tipo', field: 'type', width: 170, valueFormatter: p => AlertsTabComponent.typeLabels[p.value as AlertRuleType] ?? p.value },
    {
      headerName: 'Condizione', width: 380,
      valueGetter: p => {
        const r = p.data;
        if (!r) return '';
        if (r.type === 'PriceBelow') return `${r.productName}: ${r.useLowPrice ? 'low' : 'trend'} ≤ ${r.threshold} €`;
        if (r.type === 'PriceDrop') return `${r.productName}: calo ≥ ${r.threshold}% in 7 giorni`;
        return `"Apri" con resa ≥ ${r.threshold}%${r.setCode ? ' · ' + r.setCode : ''}${r.category ? ' · ' + r.category : ''}`;
      }
    },
    { headerName: 'Vera ora per', field: 'matchingCount', width: 120, type: 'numericColumn',
      headerTooltip: 'Prodotti per cui la condizione è vera dall\'ultima valutazione (già avvisati)' },
    { headerName: 'Email', field: 'sendEmail', width: 85, valueFormatter: p => p.value ? 'sì' : 'no' },
    { headerName: 'Attiva', field: 'isActive', width: 85, valueFormatter: p => p.value ? 'sì' : 'no' },
    { headerName: 'Ultima valutazione', field: 'lastEvaluatedAt', width: 160, valueFormatter: p => p.value ? new Date(p.value).toLocaleString('it-IT') : '' },
    {
      headerName: '', width: 90, sortable: false, filter: false,
      cellRenderer: () => `<button class="row-action edit" title="Modifica"><i class="material-icons" style="font-size:18px">edit</i></button>
                           <button class="row-action delete" title="Elimina"><i class="material-icons" style="font-size:18px">delete</i></button>`,
      onCellClicked: p => {
        const target = p.event?.target as HTMLElement | null;
        if (!p.data || !target) return;
        if (target.closest('.edit')) this.edit(p.data);
        if (target.closest('.delete')) this.remove(p.data);
      }
    }
  ];

  notificationColumns: ColDef<AlertNotification>[] = [
    { headerName: 'Quando', field: 'createdAt', width: 150, sort: 'desc', valueFormatter: p => p.value ? new Date(p.value).toLocaleString('it-IT') : '',
      cellClass: p => p.data?.readAt ? '' : 'unread' },
    { headerName: 'Avviso', field: 'title', width: 380, cellClass: p => p.data?.readAt ? '' : 'unread' },
    { headerName: 'Dettaglio', field: 'message', flex: 1, minWidth: 300, tooltipField: 'message' },
    {
      headerName: 'Email', width: 120,
      valueGetter: p => !p.data?.emailRequested ? '—' : p.data.emailSentAt ? 'inviata' : p.data.emailError ? 'non inviata' : 'in attesa',
      tooltipValueGetter: p => p.data?.emailError ?? ''
    }
  ];

  defaultColDef: ColDef = { sortable: true, resizable: true, filter: true };

  constructor(private purchasing: PurchasingService, private snackBar: MatSnackBar) { }

  ngOnInit() {
    this.load();
  }

  thresholdLabel(): string {
    return this.form.type === 'PriceBelow' ? 'Soglia €' : this.form.type === 'PriceDrop' ? 'Calo minimo %' : 'Resa minima %';
  }

  load() {
    this.purchasing.getAlertRules().subscribe({ next: rules => this.rules.set(rules), error: err => this.error('regole', err) });
    this.purchasing.getNotifications(200).subscribe({
      next: list => { this.notifications.set(list.items); this.unread.set(list.unread); this.emailConfigured.set(list.emailConfigured); },
      error: err => this.error('avvisi', err)
    });
  }

  save() {
    if (!this.form.name || this.form.threshold == null) {
      this.snackBar.open('Nome e soglia sono obbligatori', 'Chiudi', { duration: 4000 });
      return;
    }
    const input: AlertRuleInput = {
      name: this.form.name,
      type: this.form.type,
      sealedProductId: this.form.type === 'OpeningOpportunity' ? null : this.form.sealedProductId,
      setCode: this.form.type === 'OpeningOpportunity' && this.form.onlyThisRelease ? this.setCode : null,
      category: this.form.type === 'OpeningOpportunity' && this.form.category ? this.form.category : null,
      threshold: this.form.threshold,
      useLowPrice: this.form.useLowPrice,
      isActive: this.form.isActive,
      sendEmail: this.form.sendEmail
    };
    this.purchasing.saveAlertRule(input, this.form.id).subscribe({
      next: () => { this.snackBar.open('Regola salvata', 'Chiudi', { duration: 3000 }); this.reset(); this.load(); },
      error: err => this.error('salvataggio', err)
    });
  }

  edit(rule: AlertRule) {
    this.form = {
      id: rule.id, name: rule.name, type: rule.type, sealedProductId: rule.sealedProductId ?? null,
      onlyThisRelease: !!rule.setCode, category: rule.category ?? '', threshold: rule.threshold,
      useLowPrice: rule.useLowPrice, isActive: rule.isActive, sendEmail: rule.sendEmail
    };
    if (rule.sealedProductId && !this.products.some(p => p.id === rule.sealedProductId)) {
      this.products = [...this.products, { id: rule.sealedProductId, name: rule.productName ?? '' } as SealedProductAnalysis];
    }
  }

  remove(rule: AlertRule) {
    if (!confirm(`Eliminare la regola "${rule.name}"? Gli avvisi già emessi restano.`)) return;
    this.purchasing.deleteAlertRule(rule.id).subscribe({ next: () => this.load(), error: err => this.error('eliminazione', err) });
  }

  evaluate() {
    this.isEvaluating.set(true);
    this.purchasing.evaluateAlerts().subscribe({
      next: r => {
        this.isEvaluating.set(false);
        this.snackBar.open(`Regole valutate: ${r.rulesEvaluated}, avvisi nuovi: ${r.newNotifications} (email: ${r.email})`, 'Chiudi', { duration: 6000 });
        this.load();
      },
      error: err => { this.isEvaluating.set(false); this.error('valutazione', err); }
    });
  }

  testEmail() {
    this.purchasing.sendTestEmail().subscribe({
      next: () => this.snackBar.open('Email di prova inviata: controlla la casella', 'Chiudi', { duration: 6000 }),
      error: err => this.error('email di prova', err)
    });
  }

  markAllRead() {
    this.purchasing.markNotificationsRead().subscribe({ next: () => this.load(), error: err => this.error('avvisi', err) });
  }

  openNotification(notification?: AlertNotification) {
    if (!notification) return;
    if (!notification.readAt) {
      this.purchasing.markNotificationsRead(notification.id).subscribe({ next: () => this.load() });
    }
    if (notification.setCode) this.openRelease.emit(notification.setCode);
  }

  reset() {
    this.form = this.emptyForm();
  }

  private emptyForm(): RuleForm {
    return { name: '', type: 'PriceBelow', sealedProductId: null, onlyThisRelease: false, category: '', threshold: null,
      useLowPrice: false, isActive: true, sendEmail: true };
  }

  private error(what: string, err: { error?: { message?: string }; message?: string }) {
    this.snackBar.open(`Errore (${what}): ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 });
  }
}
