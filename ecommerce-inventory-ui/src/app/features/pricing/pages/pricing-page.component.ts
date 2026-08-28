import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, GridApi, GridReadyEvent } from 'ag-grid-community';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatTabsModule } from '@angular/material/tabs';
import { MatDividerModule } from '@angular/material/divider';
import {
  PricingService, PricingProfile, PricingRule, PricingRunReport,
  PricingRunSummary, CoverageReport, PriceChange, PRICING_OUTCOMES
} from '../services/pricing.service';

@Component({
  selector: 'app-pricing-page',
  standalone: true,
  imports: [
    CommonModule, FormsModule, AgGridAngular,
    MatCardModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatCheckboxModule, MatSlideToggleModule,
    MatProgressSpinnerModule, MatSnackBarModule, MatIconModule,
    MatTooltipModule, MatTabsModule, MatDividerModule
  ],
  template: `
    <div class="pricing-container">
      <mat-tab-group>

        <!-- ============ REGOLE ============ -->
        <mat-tab label="Regole">

          <!-- Stati espliciti: un'area bianca senza spiegazione lascia l'utente
               senza sapere se sta caricando, se è rotto o se manca il profilo. -->
          <div class="tab-content" *ngIf="!profile() && loadingProfile()">
            <mat-card>
              <mat-card-content class="state-card">
                <mat-spinner diameter="28"></mat-spinner>
                <span>Caricamento del profilo di pricing…</span>
              </mat-card-content>
            </mat-card>
          </div>

          <div class="tab-content" *ngIf="!profile() && !loadingProfile()">
            <mat-card class="state-error">
              <mat-card-content class="state-card">
                <mat-icon color="warn">error_outline</mat-icon>
                <div>
                  <strong>{{ loadError() || 'Nessun profilo di pricing disponibile' }}</strong>
                  <p class="hint">
                    Il profilo predefinito viene creato all'avvio dell'applicazione. Se l'errore
                    persiste, controlla che il servizio sia raggiungibile e riprova.
                  </p>
                </div>
                <button mat-stroked-button (click)="loadProfile()">
                  <mat-icon>refresh</mat-icon> Riprova
                </button>
              </mat-card-content>
            </mat-card>
          </div>

          <div class="tab-content" *ngIf="profile() as p">

            <mat-card class="mode-card" [class.live]="!p.dryRun">
              <mat-card-content>
                <div class="mode-row">
                  <mat-slide-toggle
                    [checked]="!p.dryRun"
                    (change)="toggleDryRun($event.checked)"
                    [disabled]="saving()">
                    <strong>{{ p.dryRun ? 'Simulazione (dry-run)' : 'Attivo: scrive su Card Trader' }}</strong>
                  </mat-slide-toggle>
                  <span class="mode-hint">
                    {{ p.dryRun
                        ? 'I prezzi vengono calcolati e registrati, ma nulla viene modificato su Card Trader.'
                        : 'Le variazioni calcolate vengono applicate davvero alle tue inserzioni.' }}
                  </span>
                </div>
              </mat-card-content>
            </mat-card>

            <mat-card>
              <mat-card-header><mat-card-title>Guardrail</mat-card-title></mat-card-header>
              <mat-card-content>
                <div class="field-row">
                  <mat-form-field appearance="outline">
                    <mat-label>Prezzo minimo (€)</mat-label>
                    <input matInput type="number" step="0.01" [(ngModel)]="p.minPrice">
                    <mat-hint>Nessuna carta scenderà mai sotto questo valore</mat-hint>
                  </mat-form-field>

                  <mat-form-field appearance="outline">
                    <mat-label>Variazione massima per esecuzione (%)</mat-label>
                    <input matInput type="number" step="1" [(ngModel)]="p.maxChangePercentPerRun">
                    <mat-hint>Oltre questa soglia la variazione viene registrata ma non applicata</mat-hint>
                  </mat-form-field>

                  <mat-form-field appearance="outline">
                    <mat-label>Offerte comparabili minime</mat-label>
                    <input matInput type="number" step="1" [(ngModel)]="p.minComparableOffers">
                    <mat-hint>Sotto questo numero il prezzo non viene toccato</mat-hint>
                  </mat-form-field>
                </div>
              </mat-card-content>
            </mat-card>

            <mat-card>
              <mat-card-header>
                <mat-card-title>Venditori di riferimento</mat-card-title>
                <mat-card-subtitle>
                  Card Trader non espone il numero di recensioni dei venditori, quindi un filtro
                  sul feedback non è realizzabile. Lo scarto delle offerte anomale copre lo stesso
                  bisogno: esclude il prezzo fuori scala di chiunque lo abbia pubblicato.
                </mat-card-subtitle>
              </mat-card-header>
              <mat-card-content>
                <div class="checkbox-row">
                  <mat-checkbox [(ngModel)]="p.includeProSellers">Venditori professionali</mat-checkbox>
                  <mat-checkbox [(ngModel)]="p.includeNormalSellers">Venditori privati</mat-checkbox>
                  <mat-checkbox [(ngModel)]="p.excludeVacationSellers">Escludi chi è in vacanza</mat-checkbox>
                  <mat-checkbox [(ngModel)]="p.enableOutlierRejection">Scarta le offerte anomale</mat-checkbox>
                </div>

                <div class="field-row">
                  <mat-form-field appearance="outline">
                    <mat-label>Paesi ammessi</mat-label>
                    <input matInput [(ngModel)]="p.countryCodesCsv" placeholder="IT,ES,FR — vuoto = tutti">
                    <mat-hint>Codici ISO separati da virgola</mat-hint>
                  </mat-form-field>

                  <mat-form-field appearance="outline">
                    <mat-label>Capacità minima venditore (24h)</mat-label>
                    <input matInput type="number" [(ngModel)]="p.minSellerDailyCapacity" placeholder="vuoto = nessun filtro">
                    <mat-hint>Proxy della dimensione, al posto delle recensioni</mat-hint>
                  </mat-form-field>

                  <mat-form-field appearance="outline">
                    <mat-label>Soglia anomalie (MAD)</mat-label>
                    <input matInput type="number" step="0.5" [(ngModel)]="p.outlierMadThreshold">
                    <mat-hint>Più bassa = più severa</mat-hint>
                  </mat-form-field>
                </div>
              </mat-card-content>
            </mat-card>

            <mat-card>
              <mat-card-header>
                <mat-card-title>Regole per fascia di prezzo</mat-card-title>
                <mat-card-subtitle>La fascia si applica al prezzo attuale della tua carta</mat-card-subtitle>
              </mat-card-header>
              <mat-card-content>
                <table class="rules-table">
                  <thead>
                    <tr>
                      <th>Da €</th><th>A €</th><th>Riferimento</th><th>Posizione</th>
                      <th>Scostamento €</th><th>Scostamento %</th>
                      <th>Può alzare</th><th>Può abbassare</th><th></th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr *ngFor="let r of p.rules; let i = index">
                      <td><input type="number" step="0.01" [(ngModel)]="r.fromPrice" [ngModelOptions]="{standalone: true}"></td>
                      <td><input type="number" step="0.01" [(ngModel)]="r.toPrice" [ngModelOptions]="{standalone: true}"></td>
                      <td>
                        <select [(ngModel)]="r.referenceMode" [ngModelOptions]="{standalone: true}">
                          <option value="NthLowestOffer">N-esima più bassa</option>
                          <option value="LowestOffer">Più bassa</option>
                          <option value="MedianOffer">Mediana</option>
                          <option value="AverageOffer">Media</option>
                          <option value="AverageOfLowestN">Media delle N più basse</option>
                        </select>
                      </td>
                      <td><input type="number" [(ngModel)]="r.position" [ngModelOptions]="{standalone: true}"></td>
                      <td><input type="number" step="0.01" [(ngModel)]="r.adjustmentAmount" [ngModelOptions]="{standalone: true}"></td>
                      <td><input type="number" step="0.1" [(ngModel)]="r.adjustmentPercent" [ngModelOptions]="{standalone: true}"></td>
                      <td class="center"><mat-checkbox [(ngModel)]="r.canIncrease" [ngModelOptions]="{standalone: true}"></mat-checkbox></td>
                      <td class="center"><mat-checkbox [(ngModel)]="r.canDecrease" [ngModelOptions]="{standalone: true}"></mat-checkbox></td>
                      <td class="center">
                        <button mat-icon-button color="warn" (click)="removeRule(i)" matTooltip="Elimina regola">
                          <mat-icon>delete</mat-icon>
                        </button>
                      </td>
                    </tr>
                  </tbody>
                </table>

                <div class="actions">
                  <button mat-stroked-button (click)="addRule()">
                    <mat-icon>add</mat-icon> Aggiungi regola
                  </button>
                  <button mat-raised-button color="primary" (click)="save()" [disabled]="saving()">
                    <mat-icon>save</mat-icon> Salva profilo
                  </button>
                </div>
              </mat-card-content>
            </mat-card>
          </div>
        </mat-tab>

        <!-- ============ ANTEPRIMA ============ -->
        <mat-tab label="Anteprima">
          <div class="tab-content">
            <mat-card>
              <mat-card-header>
                <mat-card-title>Prova le regole senza toccare i prezzi</mat-card-title>
                <mat-card-subtitle>
                  Calcola cosa cambierebbe sulle carte di maggior valore. Non scrive nulla,
                  né su Card Trader né sullo storico, indipendentemente dalla modalità del profilo.
                </mat-card-subtitle>
              </mat-card-header>
              <mat-card-content>
                <div class="field-row">
                  <mat-form-field appearance="outline">
                    <mat-label>Quante carte</mat-label>
                    <input matInput type="number" [(ngModel)]="previewLimit">
                  </mat-form-field>
                  <button mat-raised-button color="primary" (click)="runPreview()" [disabled]="loading()">
                    <mat-icon>visibility</mat-icon> Calcola anteprima
                  </button>
                  <mat-spinner diameter="28" *ngIf="loading()"></mat-spinner>
                </div>

                <div class="hint" *ngIf="loading()">
                  Ogni carta richiede una chiamata a Card Trader, soggetta al limite di 20 al minuto.
                </div>

                <ng-container *ngIf="report() as rep">
                  <div class="kpi-row">
                    <div class="kpi"><span class="kpi-value">{{ rep.evaluatedCount }}</span><span class="kpi-label">valutate</span></div>
                    <div class="kpi"><span class="kpi-value">{{ rep.appliedCount }}</span><span class="kpi-label">applicate</span></div>
                    <div class="kpi"><span class="kpi-value">{{ rep.simulatedCount }}</span><span class="kpi-label">simulate</span></div>
                    <div class="kpi"><span class="kpi-value">{{ rep.noChangeCount }}</span><span class="kpi-label">invariate</span></div>
                    <div class="kpi"><span class="kpi-value">{{ rep.skippedCount }}</span><span class="kpi-label">saltate</span></div>
                    <div class="kpi" [class.negative]="rep.totalPriceDelta < 0">
                      <span class="kpi-value">{{ euroPublic(rep.totalPriceDelta) }}</span>
                      <span class="kpi-label">variazione totale</span>
                    </div>
                  </div>

                  <ag-grid-angular
                    class="ag-theme-quartz grid"
                    [rowData]="rep.changes"
                    [columnDefs]="changeColumns"
                    [defaultColDef]="defaultColDef"
                    [pagination]="true"
                    [paginationPageSize]="25"
                    (gridReady)="onGridReady($event)">
                  </ag-grid-angular>
                </ng-container>
              </mat-card-content>
            </mat-card>
          </div>
        </mat-tab>

        <!-- ============ COPERTURA ============ -->
        <mat-tab label="Copertura">
          <div class="tab-content">
            <mat-card>
              <mat-card-header>
                <mat-card-title>Da quanto tempo le carte non vengono valutate</mat-card-title>
                <mat-card-subtitle>
                  È la risposta misurabile al difetto principale dell'autopricer nativo:
                  qui si vede quali carte restano indietro, e da quanto.
                </mat-card-subtitle>
              </mat-card-header>
              <mat-card-content>
                <button mat-stroked-button (click)="loadCoverage()">
                  <mat-icon>refresh</mat-icon> Aggiorna
                </button>

                <ng-container *ngIf="coverage() as cov">
                  <p class="hint">
                    {{ num(cov.blueprintTotali) }} blueprint a magazzino,
                    di cui {{ num(cov.maiValutati) }} mai valutati dall'autopricer.
                  </p>
                  <table class="coverage-table">
                    <thead>
                      <tr>
                        <th>Fascia</th><th>Blueprint</th><th>Mai valutati</th>
                        <th>Ultime 24h</th><th>Ultimi 7 giorni</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr *ngFor="let f of cov.fasce">
                        <td>{{ f.fascia }}</td>
                        <td>{{ num(f.blueprint) }}</td>
                        <td>{{ num(f.maiValutati) }}</td>
                        <td>{{ num(f.valutatiUltime24h) }}</td>
                        <td>{{ num(f.valutatiUltimi7Giorni) }}</td>
                      </tr>
                    </tbody>
                  </table>
                </ng-container>
              </mat-card-content>
            </mat-card>
          </div>
        </mat-tab>

        <!-- ============ STORICO ============ -->
        <mat-tab label="Storico">
          <div class="tab-content">
            <mat-card>
              <mat-card-header><mat-card-title>Esecuzioni</mat-card-title></mat-card-header>
              <mat-card-content>
                <button mat-stroked-button (click)="loadRuns()">
                  <mat-icon>refresh</mat-icon> Aggiorna
                </button>
                <p class="hint">Clicca una esecuzione per vedere i calcoli carta per carta.</p>
                <ag-grid-angular
                  class="ag-theme-quartz grid"
                  [rowData]="runs()"
                  [columnDefs]="runColumns"
                  [defaultColDef]="defaultColDef"
                  [pagination]="true"
                  [paginationPageSize]="20"
                  [rowSelection]="'single'"
                  (rowClicked)="selectRun($event.data)">
                </ag-grid-angular>
              </mat-card-content>
            </mat-card>

            <!-- ---- dettaglio della esecuzione selezionata ---- -->
            <mat-card class="detail-card" *ngIf="selectedRun() as run">
              <mat-card-header>
                <mat-card-title>
                  Calcoli dell'esecuzione del {{ dateTime(run.startedAt) }}
                </mat-card-title>
                <mat-card-subtitle>
                  {{ triggerLabel(run.trigger) }}<span *ngIf="run.dryRun"> · simulazione, nessun prezzo scritto su Card Trader</span>
                </mat-card-subtitle>
              </mat-card-header>
              <mat-card-content>
                <div class="field-row">
                  <mat-form-field appearance="outline">
                    <mat-label>Esito</mat-label>
                    <mat-select [(ngModel)]="changesOutcome" (selectionChange)="loadRunChanges()">
                      <mat-option [value]="''">Tutti</mat-option>
                      <mat-option *ngFor="let o of outcomes" [value]="o.value">{{ o.label }}</mat-option>
                    </mat-select>
                  </mat-form-field>
                  <button mat-stroked-button (click)="loadRunChanges()" [disabled]="loadingChanges()">
                    <mat-icon>refresh</mat-icon> Aggiorna
                  </button>
                  <button mat-button (click)="closeRun()">
                    <mat-icon>close</mat-icon> Chiudi
                  </button>
                  <mat-spinner diameter="28" *ngIf="loadingChanges()"></mat-spinner>
                </div>

                <p class="hint" *ngIf="!loadingChanges() && changesTotal() > changes().length">
                  Mostrate le {{ changes().length }} variazioni di importo maggiore su {{ num(changesTotal()) }} totali.
                  Restringi con il filtro per esito per vedere le altre.
                </p>
                <p class="hint" *ngIf="!loadingChanges() && changesTotal() > 0 && changesTotal() <= changes().length">
                  {{ num(changesTotal()) }} carte valutate.
                </p>
                <p class="hint" *ngIf="!loadingChanges() && changesTotal() === 0">
                  Nessuna carta con questo esito in questa esecuzione.
                </p>

                <ag-grid-angular
                  *ngIf="changes().length > 0"
                  class="ag-theme-quartz grid"
                  [rowData]="changes()"
                  [columnDefs]="changeColumns"
                  [defaultColDef]="defaultColDef"
                  [pagination]="true"
                  [paginationPageSize]="25">
                </ag-grid-angular>
              </mat-card-content>
            </mat-card>
          </div>
        </mat-tab>

      </mat-tab-group>
    </div>
  `,
  styleUrls: ['./pricing-page.component.css']
})
export class PricingPageComponent implements OnInit {
  profile = signal<PricingProfile | null>(null);
  report = signal<PricingRunReport | null>(null);
  runs = signal<PricingRunSummary[]>([]);
  coverage = signal<CoverageReport | null>(null);
  loading = signal(false);
  saving = signal(false);
  loadingProfile = signal(true);
  loadError = signal<string | null>(null);

  // Dettaglio della esecuzione selezionata nello storico: è il registro su cui si fanno
  // le verifiche a campione prima di togliere il dry-run.
  selectedRun = signal<PricingRunSummary | null>(null);
  changes = signal<PriceChange[]>([]);
  changesTotal = signal(0);
  loadingChanges = signal(false);
  changesOutcome = '';
  readonly outcomes = PRICING_OUTCOMES;

  previewLimit = 15;
  private gridApi?: GridApi;

  defaultColDef: ColDef = { sortable: true, filter: true, resizable: true };

  changeColumns: ColDef[] = [
    { field: 'cardName', headerName: 'Carta', flex: 2, minWidth: 180 },
    { field: 'oldPrice', headerName: 'Attuale', width: 110, valueFormatter: p => this.euro(p.value) },
    { field: 'proposedPrice', headerName: 'Proposto', width: 115, valueFormatter: p => this.euro(p.value) },
    {
      field: 'delta', headerName: 'Variazione', width: 120,
      valueFormatter: p => (p.value > 0 ? '+' : '') + this.euro(p.value),
      cellStyle: p => p.value > 0 ? { color: '#2e7d32' } : p.value < 0 ? { color: '#c62828' } : null
    },
    { field: 'comparableOffersCount', headerName: 'Offerte', width: 100 },
    { field: 'outliersRejectedCount', headerName: 'Anomale', width: 105 },
    {
      field: 'outcome', headerName: 'Esito', width: 165,
      valueFormatter: p => this.outcomeLabel(p.value)
    },
    {
      // La riga sopravvive alla carta: senza questa colonna una valutazione riferita a una carta
      // ormai venduta sarebbe indistinguibile da una ancora a magazzino.
      field: 'inventoryItemId', headerName: 'Magazzino', width: 130,
      valueFormatter: p => p.value == null ? 'Non più a magazzino' : 'Presente',
      cellStyle: p => p.value == null ? { color: '#8a6d3b' } : null
    },
    { field: 'reason', headerName: 'Motivo', flex: 3, minWidth: 300, tooltipField: 'reason' }
  ];

  runColumns: ColDef[] = [
    { field: 'startedAt', headerName: 'Inizio', width: 175, valueFormatter: p => this.dateTime(p.value) },
    { field: 'trigger', headerName: 'Origine', width: 140, valueFormatter: p => this.triggerLabel(p.value) },
    { field: 'dryRun', headerName: 'Simulazione', width: 125, valueFormatter: p => p.value ? 'Sì' : 'No' },
    { field: 'plannedCount', headerName: 'Previste', width: 105 },
    { field: 'evaluatedCount', headerName: 'Valutate', width: 105 },
    { field: 'coveragePercent', headerName: 'Copertura', width: 115, valueFormatter: p => `${p.value}%` },
    { field: 'appliedCount', headerName: 'Applicate', width: 110 },
    { field: 'skippedCount', headerName: 'Saltate', width: 100 },
    { field: 'failedCount', headerName: 'Fallite', width: 95 },
    { field: 'totalPriceDelta', headerName: 'Variazione', width: 125, valueFormatter: p => this.euro(p.value) }
  ];

  constructor(private pricingService: PricingService, private snackBar: MatSnackBar) { }

  ngOnInit(): void {
    this.loadProfile();
    this.loadRuns();
    this.loadCoverage();
  }

  loadProfile(): void {
    this.loadingProfile.set(true);
    this.loadError.set(null);

    this.pricingService.getProfiles().subscribe({
      next: profiles => {
        this.profile.set(profiles[0] ?? null);
        this.loadingProfile.set(false);
        if (profiles.length === 0) {
          this.loadError.set('Nessun profilo di pricing presente a database');
        }
      },
      error: err => {
        this.loadingProfile.set(false);
        // Il messaggio resta visibile nella pagina: uno snackbar che sparisce dopo
        // pochi secondi lascia l'utente davanti a un'area vuota e inspiegata.
        this.loadError.set(
          err?.status === 0
            ? "Impossibile contattare il servizio: verifica che l'API sia raggiungibile"
            : `Errore ${err?.status ?? ''} nel caricamento del profilo di pricing`.trim());
        this.snackBar.open('Impossibile caricare il profilo di pricing', 'Chiudi', { duration: 6000 });
      }
    });
  }

  loadRuns(): void {
    this.pricingService.getRuns().subscribe({
      next: runs => this.runs.set(runs),
      error: () => this.snackBar.open('Impossibile caricare lo storico', 'Chiudi', { duration: 4000 })
    });
  }

  loadCoverage(): void {
    this.pricingService.getCoverage().subscribe({
      next: cov => this.coverage.set(cov),
      error: () => this.snackBar.open('Impossibile caricare la copertura', 'Chiudi', { duration: 4000 })
    });
  }

  /** Apre il dettaglio di una esecuzione dallo storico. */
  selectRun(run: PricingRunSummary | undefined): void {
    if (!run) return;

    // Ricliccare la riga già aperta non deve azzerare il filtro scelto.
    if (this.selectedRun()?.id !== run.id) {
      this.changesOutcome = '';
      this.changes.set([]);
      this.changesTotal.set(0);
    }

    this.selectedRun.set(run);
    this.loadRunChanges();
  }

  closeRun(): void {
    this.selectedRun.set(null);
    this.changes.set([]);
    this.changesTotal.set(0);
    this.changesOutcome = '';
  }

  loadRunChanges(): void {
    const run = this.selectedRun();
    if (!run) return;

    this.loadingChanges.set(true);
    this.pricingService.getRunChanges(run.id, this.changesOutcome || undefined).subscribe({
      next: page => {
        this.changes.set(page.items);
        this.changesTotal.set(page.totalCount);
        this.loadingChanges.set(false);
      },
      error: () => {
        this.loadingChanges.set(false);
        this.changes.set([]);
        this.changesTotal.set(0);
        this.snackBar.open('Impossibile caricare i calcoli di questa esecuzione', 'Chiudi', { duration: 5000 });
      }
    });
  }

  runPreview(): void {
    const p = this.profile();
    if (!p) return;

    this.loading.set(true);
    this.pricingService.preview(p.id, this.previewLimit).subscribe({
      next: rep => {
        this.report.set(rep);
        this.loading.set(false);
        this.snackBar.open(`Anteprima completata su ${rep.evaluatedCount} carte`, 'Chiudi', { duration: 3000 });
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Errore durante il calcolo dell\'anteprima', 'Chiudi', { duration: 5000 });
      }
    });
  }

  /**
   * Il passaggio da simulazione a scrittura reale è la sola azione di questa pagina
   * che tocca i prezzi veri: viene salvato subito, così lo stato mostrato è quello effettivo.
   */
  toggleDryRun(liveMode: boolean): void {
    const p = this.profile();
    if (!p) return;

    this.saving.set(true);
    this.pricingService.updateProfile(p.id, { dryRun: !liveMode }).subscribe({
      next: updated => {
        this.profile.set(updated);
        this.saving.set(false);
        this.snackBar.open(
          liveMode
            ? 'Autopricer attivo: le variazioni verranno applicate su Card Trader'
            : 'Autopricer in simulazione: nessun prezzo verrà modificato',
          'Chiudi', { duration: 5000 });
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Impossibile cambiare modalità', 'Chiudi', { duration: 4000 });
      }
    });
  }

  addRule(): void {
    const p = this.profile();
    if (!p) return;

    p.rules.push({
      fromPrice: 0.02, toPrice: 1, referenceMode: 'NthLowestOffer', position: 2,
      adjustmentAmount: -0.01, adjustmentPercent: 0,
      canIncrease: true, canDecrease: true, priority: p.rules.length, isActive: true
    });
    this.profile.set({ ...p });
  }

  removeRule(index: number): void {
    const p = this.profile();
    if (!p) return;

    p.rules.splice(index, 1);
    this.profile.set({ ...p });
  }

  save(): void {
    const p = this.profile();
    if (!p) return;

    this.saving.set(true);
    this.pricingService.updateProfile(p.id, p).subscribe({
      next: updated => {
        this.profile.set(updated);
        this.saving.set(false);
        this.snackBar.open('Profilo salvato', 'Chiudi', { duration: 3000 });
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Errore nel salvataggio del profilo', 'Chiudi', { duration: 5000 });
      }
    });
  }

  onGridReady(event: GridReadyEvent): void {
    this.gridApi = event.api;
  }

  /**
   * L'applicazione non registra il locale italiano, quindi le pipe Angular formattano
   * all'inglese. Le motivazioni arrivano dal server già in italiano: si formatta
   * esplicitamente qui per non mostrare "140.37 €" accanto a "140,37 €".
   */
  private euro(value: number | null | undefined): string {
    return value == null
      ? ''
      : `${value.toLocaleString('it-IT', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} €`;
  }

  /** Variante pubblica per il template. */
  euroPublic(value: number | null | undefined): string {
    return this.euro(value);
  }

  /** Interi con separatore delle migliaia all'italiana. */
  num(value: number | null | undefined): string {
    return value == null ? '' : value.toLocaleString('it-IT');
  }

  dateTime(value: string): string {
    return value ? new Date(value).toLocaleString('it-IT') : '';
  }

  private outcomeLabel(outcome: string): string {
    const labels: Record<string, string> = {
      Applied: 'Applicato',
      SimulatedDryRun: 'Simulato',
      NoChangeNeeded: 'Già allineato',
      NoMatchingRule: 'Nessuna regola',
      InsufficientOffers: 'Offerte insufficienti',
      BlockedByGuardrail: 'Bloccato dal guardrail',
      BlockedByDirection: 'Direzione non consentita',
      Failed: 'Errore'
    };
    return labels[outcome] ?? outcome;
  }

  triggerLabel(trigger: string): string {
    const labels: Record<string, string> = {
      Scheduled: 'Notturna',
      OrderReceived: 'Vendita',
      Manual: 'Manuale',
      Preview: 'Anteprima'
    };
    return labels[trigger] ?? trigger;
  }
}
