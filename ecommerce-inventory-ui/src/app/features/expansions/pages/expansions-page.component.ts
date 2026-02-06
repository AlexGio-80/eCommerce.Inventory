import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, ModuleRegistry, AllCommunityModule, GridApi, GridReadyEvent } from 'ag-grid-community';
import { ExpansionsService, Expansion, SyncBlueprintsResponse } from '../services/expansions.service';
import { GridStateService } from '../../../core/services/grid-state.service';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatMenuModule } from '@angular/material/menu';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatTooltipModule } from '@angular/material/tooltip';

// Register AG Grid modules
ModuleRegistry.registerModules([AllCommunityModule]);

@Component({
  selector: 'app-expansions-page',
  standalone: true,
  imports: [
    CommonModule,
    AgGridAngular,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatMenuModule,
    MatIconModule,
    MatCheckboxModule,
    MatTooltipModule
  ],
  template: `
    <div class="expansions-container">
      <h1>Gestione Espansioni</h1>

      <!-- Header Card with Selected Expansion Details -->
      <mat-card class="header-card" *ngIf="selectedExpansion()">
        <mat-card-header>
          <div class="header-title-group">
            <mat-card-title>{{ selectedExpansion()?.name }}</mat-card-title>
            <mat-card-subtitle>{{ selectedExpansion()?.gameName }} ({{ selectedExpansion()?.gameCode }})</mat-card-subtitle>
            <div class="expansion-meta">
              <span class="meta-tag">ID: {{ selectedExpansion()?.id }}</span>
              <span class="meta-tag">CT ID: {{ selectedExpansion()?.cardTraderId }}</span>
              <span class="meta-tag">Code: {{ selectedExpansion()?.code }}</span>
            </div>
          </div>
        </mat-card-header>
        <mat-card-content>
          <div class="expansion-dashboard">
            <!-- Market Value Section -->
            <div class="dashboard-section">
              <h3>Valore Mercato</h3>
              <div class="stat-grid">
                <div class="stat-item">
                  <span class="stat-label">Valore Medio Carta</span>
                  <span class="stat-value secondary">€{{ selectedExpansion()?.averageCardValue?.toFixed(2) || '0.00' }}</span>
                </div>
                <div class="stat-item">
                  <span class="stat-label">Valore Totale (Min)</span>
                  <span class="stat-value secondary">€{{ selectedExpansion()?.totalMinPrice?.toFixed(2) || '0.00' }}</span>
                </div>
                <div class="stat-item full-width">
                  <span class="stat-label">Ultima Analisi</span>
                  <span class="stat-value text-small">{{ (selectedExpansion()?.lastValueAnalysisUpdate | date:'dd/MM/yyyy HH:mm') || 'Mai' }}</span>
                </div>
              </div>
            </div>

            <!-- Financials Section -->
            <div class="dashboard-section">
              <h3>Performance Vendite</h3>
              <div class="stat-grid">
                <div class="stat-item">
                  <span class="stat-label">Vendite Totali</span>
                  <span class="stat-value">€{{ selectedExpansion()?.totalSales?.toFixed(2) || '0.00' }}</span>
                </div>
                 <div class="stat-item">
                  <span class="stat-label">Spesa Totale</span>
                  <span class="stat-value">€{{ selectedExpansion()?.totalAmountSpent?.toFixed(2) || '0.00' }}</span>
                </div>
                 <div class="stat-item">
                  <span class="stat-label">Profitto</span>
                   <span class="stat-value" 
                    [class.positive]="(selectedExpansion()?.totalProfit || 0) > 0"
                    [class.negative]="(selectedExpansion()?.totalProfit || 0) < 0">
                    €{{ selectedExpansion()?.totalProfit?.toFixed(2) || '0.00' }}
                  </span>
                </div>
                <div class="stat-item">
                  <span class="stat-label">ROI %</span>
                  <span class="stat-value" 
                    [class.positive]="(selectedExpansion()?.roiPercentage || 0) > 0"
                    [class.negative]="(selectedExpansion()?.roiPercentage || 0) < 0">
                    {{ selectedExpansion()?.roiPercentage?.toFixed(1) || '0.0' }}%
                    <mat-icon inline="true" *ngIf="(selectedExpansion()?.roiPercentage || 0) > 0">trending_up</mat-icon>
                    <mat-icon inline="true" *ngIf="(selectedExpansion()?.roiPercentage || 0) < 0">trending_down</mat-icon>
                  </span>
                </div>
              </div>
            </div>

            <!-- Rarity Stats Section -->
            <div class="dashboard-section">
              <h3>Media per Rarità</h3>
              <div class="rarity-grid">
                <div class="rarity-item common">
                  <span class="rarity-label">Comune</span>
                  <span class="rarity-value">€{{ selectedExpansion()?.avgValueCommon?.toFixed(2) || '0.00' }}</span>
                </div>
                <div class="rarity-item uncommon">
                  <span class="rarity-label">Non Comune</span>
                  <span class="rarity-value">€{{ selectedExpansion()?.avgValueUncommon?.toFixed(2) || '0.00' }}</span>
                </div>
                <div class="rarity-item rare">
                  <span class="rarity-label">Rara</span>
                  <span class="rarity-value">€{{ selectedExpansion()?.avgValueRare?.toFixed(2) || '0.00' }}</span>
                </div>
                <div class="rarity-item mythic">
                  <span class="rarity-label">Mitica</span>
                  <span class="rarity-value">€{{ selectedExpansion()?.avgValueMythic?.toFixed(2) || '0.00' }}</span>
                </div>
              </div>
            </div>
          </div>
        </mat-card-content>
        <mat-card-actions align="end">
          <button 
            mat-stroked-button 
            color="primary" 
            (click)="syncBlueprints()"
            [disabled]="isSyncing()">
            <mat-spinner *ngIf="isSyncing()" diameter="20"></mat-spinner>
            {{ isSyncing() ? 'Syncing...' : 'Sync Blueprints' }}
          </button>
          <button 
            mat-raised-button 
            color="accent" 
            (click)="analyzeValue()"
            [disabled]="isAnalyzing()">
            <mat-spinner *ngIf="isAnalyzing()" diameter="20"></mat-spinner>
            {{ isAnalyzing() ? 'Analyzing...' : 'Analizza Valore' }}
          </button>
        </mat-card-actions>
      </mat-card>

      <!-- AG-Grid Table -->
      <mat-card class="grid-card">
        <mat-card-content>
          <div class="grid-header">
            <h2>Lista Espansioni</h2>
            <div class="header-actions">
              <button mat-button color="primary" (click)="analyzeAllValues()" [disabled]="isAnalyzingAll()">
                <mat-spinner *ngIf="isAnalyzingAll()" diameter="18"></mat-spinner>
                Analizza Tutte
              </button>
            </div>
            
            <!-- Grid Options Menu -->
            <button mat-icon-button [matMenuTriggerFor]="gridMenu" matTooltip="Grid Options">
              <mat-icon>more_vert</mat-icon>
            </button>
            <mat-menu #gridMenu="matMenu">
              <button mat-menu-item [matMenuTriggerFor]="columnsMenu">
                <mat-icon>view_column</mat-icon>
                <span>Columns</span>
              </button>
              <button mat-menu-item (click)="saveGridState()">
                <mat-icon>save</mat-icon>
                <span>Save Configuration</span>
              </button>
              <button mat-menu-item (click)="resetGridState()">
                <mat-icon>refresh</mat-icon>
                <span>Reset to Defaults</span>
              </button>
            </mat-menu>

            <!-- Columns Sub-Menu -->
            <mat-menu #columnsMenu="matMenu">
              <div class="columns-menu-container" (click)="$event.stopPropagation()">
                <div *ngFor="let col of getAllColumns()" class="column-toggle-item">
                  <mat-checkbox 
                    [checked]="isColumnVisible(col.field!)"
                    (change)="toggleColumnVisibility(col.field!)">
                    {{ col.headerName }}
                  </mat-checkbox>
                </div>
              </div>
            </mat-menu>
          </div>

          <ag-grid-angular
            class="ag-theme-material"
            [rowData]="expansions()"
            [columnDefs]="columnDefs"
            [defaultColDef]="defaultColDef"
            [gridOptions]="gridOptions"
            [pagination]="true"
            [paginationPageSize]="20"
            [rowSelection]="'single'"
            (gridReady)="onGridReady($event)"
            (selectionChanged)="onSelectionChanged($event)"
            (columnMoved)="onColumnMoved()"
            (columnVisible)="onColumnVisible()"
            (sortChanged)="onSortChanged()"
            style="height: 600px; width: 100%;">
          </ag-grid-angular>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .expansions-container {
      padding: 24px;
      max-width: 1400px;
      margin: 0 auto;
    }

    h1 {
      margin-bottom: 24px;
      color: #333;
      font-weight: 400;
    }

    .header-card {
      margin-bottom: 24px;
      border-radius: 8px;
    }

    /* Header Title Group */
    .header-title-group {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .expansion-meta {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
      margin-top: 4px;
    }

    .meta-tag {
      background-color: #f5f5f5;
      color: #666;
      padding: 2px 8px;
      border-radius: 4px;
      font-size: 0.8rem;
      border: 1px solid #e0e0e0;
    }

    /* Dashboard Layout */
    .expansion-dashboard {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
      gap: 24px;
      padding: 16px 0;
    }

    .dashboard-section {
      background: #fafafa;
      padding: 16px;
      border-radius: 8px;
      border: 1px solid #eee;
    }

    .dashboard-section h3 {
      margin: 0 0 16px 0;
      color: #555;
      font-size: 1rem;
      font-weight: 500;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      border-bottom: 2px solid #ddd;
      padding-bottom: 8px;
      display: inline-block;
    }

    /* Stats Grid */
    .stat-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }

    .stat-item {
      display: flex;
      flex-direction: column;
    }

    .stat-item.full-width {
      grid-column: 1 / -1;
      margin-top: 8px;
      padding-top: 8px;
      border-top: 1px dashed #e0e0e0;
    }

    .stat-label {
      font-size: 0.85rem;
      color: #777;
      margin-bottom: 4px;
    }

    .stat-value {
      font-size: 1.2rem;
      font-weight: 600;
      color: #333;
      display: flex;
      align-items: center;
      gap: 4px;
    }

    .stat-value.secondary {
      color: #1976d2;
    }

    .stat-value.text-small {
      font-size: 0.9rem;
      font-weight: 400;
    }

    /* Financial Indicators */
    .stat-value.positive {
      color: #2e7d32; /* Green */
    }

    .stat-value.negative {
      color: #c62828; /* Red */
    }

    /* Rarity Grid */
    .rarity-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
    }

    .rarity-item {
      display: flex;
      flex-direction: column;
      padding: 8px;
      border-radius: 4px;
      background: #fff;
      border-left: 4px solid #ccc;
      box-shadow: 0 1px 3px rgba(0,0,0,0.1);
    }

    .rarity-item.common { border-left-color: #000; } /* Black/Grey */
    .rarity-item.uncommon { border-left-color: #b0bec5; } /* Silver/Blueish */
    .rarity-item.rare { border-left-color: #ffd700; } /* Gold */
    .rarity-item.mythic { border-left-color: #ff5722; } /* Orange/Red */

    .rarity-label {
      font-size: 0.75rem;
      color: #666;
      text-transform: uppercase;
    }

    .rarity-value {
      font-size: 1.1rem;
      font-weight: 500;
      color: #333;
    }

    /* General Grid & Actions */
    .header-actions {
      display: flex;
      gap: 8px;
      align-items: center;
    }

    mat-spinner {
      display: inline-block;
      margin-right: 8px;
    }

    .grid-card {
      margin-top: 24px;
    }
    
    .grid-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 16px;
    }
    
    .grid-header h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 500;
    }

    :host ::ng-deep .ag-theme-material {
      --ag-header-background-color: #3f51b5;
      --ag-header-foreground-color: white;
    }
    
    /* Column Menu Styles */
    .columns-menu-container {
      padding: 8px;
      min-width: 200px;
      max-height: 300px;
      overflow-y: auto;
    }

    .column-toggle-item {
      padding: 4px 8px;
    }

    .grid-actions {
      display: flex;
      justify-content: center;
      align-items: center;
      height: 100%;
    }

    .analysis-btn {
      background: none;
      border: none;
      cursor: pointer;
      color: #3f51b5;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 4px;
      border-radius: 50%;
      transition: background-color 0.2s;
    }

    .analysis-btn:hover {
      background-color: rgba(63, 81, 181, 0.1);
    }

    .analysis-btn mat-icon, .analysis-btn .material-icons {
      font-size: 20px;
    }

    ::ng-deep .mat-mdc-menu-content {
      padding: 0 !important;
    }
  `]
})
export class ExpansionsPageComponent implements OnInit {
  expansions = signal<Expansion[]>([]);
  selectedExpansion = signal<Expansion | null>(null);
  isSyncing = signal(false);
  isAnalyzing = signal(false);
  isAnalyzingAll = signal(false);

  private gridApi!: GridApi;
  private readonly GRID_ID = 'expansions-grid';

  columnDefs: ColDef[] = [
    { field: 'id', headerName: 'ID', width: 80, filter: 'agNumberColumnFilter' },
    { field: 'cardTraderId', headerName: 'CT ID', width: 100, filter: 'agNumberColumnFilter' },
    { field: 'name', headerName: 'Nome', flex: 2, filter: 'agTextColumnFilter' },
    { field: 'code', headerName: 'Codice', width: 120, filter: 'agTextColumnFilter' },
    { field: 'gameName', headerName: 'Gioco', flex: 1, filter: 'agTextColumnFilter' },
    {
      field: 'averageCardValue',
      headerName: 'Val. Medio',
      width: 120,
      filter: 'agNumberColumnFilter',
      valueFormatter: (params: any) => params.value ? `€${params.value.toFixed(2)}` : '-'
    },
    {
      field: 'avgValueCommon',
      headerName: 'Avg Com',
      width: 100,
      filter: 'agNumberColumnFilter',
      valueFormatter: (params: any) => params.value ? `€${params.value.toFixed(2)}` : '-'
    },
    {
      field: 'avgValueUncommon',
      headerName: 'Avg Unc',
      width: 100,
      filter: 'agNumberColumnFilter',
      valueFormatter: (params: any) => params.value ? `€${params.value.toFixed(2)}` : '-'
    },
    {
      field: 'avgValueRare',
      headerName: 'Avg Rare',
      width: 100,
      filter: 'agNumberColumnFilter',
      valueFormatter: (params: any) => params.value ? `€${params.value.toFixed(2)}` : '-'
    },
    {
      field: 'avgValueMythic',
      headerName: 'Avg Myt',
      width: 100,
      filter: 'agNumberColumnFilter',
      valueFormatter: (params: any) => params.value ? `€${params.value.toFixed(2)}` : '-'
    },
    {
      field: 'totalMinPrice',
      headerName: 'Val. Totale',
      width: 120,
      filter: 'agNumberColumnFilter',
      valueFormatter: (params: any) => params.value ? `€${params.value.toFixed(2)}` : '-'
    },
    {
      field: 'lastValueAnalysisUpdate',
      headerName: 'Ultima Analisi',
      width: 150,
      valueFormatter: (params: any) => params.value ? new Date(params.value).toLocaleString() : '-'
    },
    { field: 'gameCode', headerName: 'Codice Gioco', width: 120, filter: 'agTextColumnFilter' },
    {
      headerName: 'Azioni',
      field: 'actions',
      width: 100,
      pinned: 'right',
      cellRenderer: (params: any) => {
        if (!params.data) return '';
        return `
          <div class="grid-actions">
            <button class="analysis-btn" title="Analizza Valore">
              <i class="material-icons">analytics</i>
            </button>
          </div>
        `;
      },
      onCellClicked: (params: any) => {
        const target = params.event?.target as HTMLElement;
        if (target.classList.contains('analysis-btn') || target.closest('.analysis-btn')) {
          this.analyzeExpansionValue(params.data);
        }
      }
    }
  ];

  defaultColDef: ColDef = {
    sortable: true,
    resizable: true,
    filter: true
  };

  gridOptions = {
    suppressMenuHide: false,
    columnMenu: 'new' as const,
    suppressDragLeaveHidesColumns: true
  };

  constructor(
    private expansionsService: ExpansionsService,
    private gridStateService: GridStateService,
    private snackBar: MatSnackBar
  ) { }

  ngOnInit() {
    this.loadExpansions();
  }

  onGridReady(params: GridReadyEvent) {
    this.gridApi = params.api;

    // Restore saved grid state
    const savedState = this.gridStateService.loadGridState(this.GRID_ID);
    if (savedState) {
      if (savedState.columnState) {
        this.gridApi.applyColumnState({ state: savedState.columnState, applyOrder: true });
      }
      if (savedState.sortModel) {
        this.gridApi.applyColumnState({ state: savedState.sortModel });
      }
    }
  }

  loadExpansions() {
    this.expansionsService.getExpansions().subscribe({
      next: (data) => {
        this.expansions.set(data);
      },
      error: (error) => {
        console.error('Error loading expansions:', error);
        this.snackBar.open('Error loading expansions', 'Close', { duration: 3000 });
      }
    });
  }

  onSelectionChanged(event: any) {
    const selectedRows = event.api.getSelectedRows();
    if (selectedRows.length > 0) {
      this.selectedExpansion.set(selectedRows[0]);
    } else {
      this.selectedExpansion.set(null);
    }
  }

  syncBlueprints() {
    const expansion = this.selectedExpansion();
    if (!expansion) return;

    this.isSyncing.set(true);
    console.log(`Starting blueprint sync for expansion ${expansion.id} (${expansion.name})`);

    this.expansionsService.syncBlueprints(expansion.id).subscribe({
      next: (response: SyncBlueprintsResponse) => {
        this.isSyncing.set(false);
        this.snackBar.open(response.message, 'Close', { duration: 5000 });
      },
      error: (error) => {
        this.isSyncing.set(false);
        console.error('Error syncing blueprints:', error);
        this.snackBar.open(`Error: ${error.error?.error || 'Failed to sync blueprints'}`, 'Close', { duration: 5000 });
      }
    });
  }

  analyzeValue() {
    const expansion = this.selectedExpansion();
    if (!expansion) return;
    this.analyzeExpansionValue(expansion);
  }

  analyzeExpansionValue(expansion: Expansion) {
    if (this.isAnalyzing()) return;

    this.isAnalyzing.set(true);
    this.expansionsService.analyzeValue(expansion.id).subscribe({
      next: () => {
        this.isAnalyzing.set(false);
        this.snackBar.open(`Analisi per ${expansion.name} completata`, 'Chiudi', { duration: 3000 });

        // Refresh only the single expansion to maintain focus and selection
        this.expansionsService.getExpansion(expansion.id).subscribe(updatedExpansion => {
          const currentList = this.expansions();
          const index = currentList.findIndex(e => e.id === updatedExpansion.id);
          if (index !== -1) {
            const newList = [...currentList];
            newList[index] = updatedExpansion;
            this.expansions.set(newList);

            // Update selected expansion if it's the one we just analyzed
            if (this.selectedExpansion()?.id === updatedExpansion.id) {
              this.selectedExpansion.set(updatedExpansion);
            }

            // Update grid row data and maintain selection
            if (this.gridApi) {
              // Apply the transaction to update the row data
              this.gridApi.applyTransaction({ update: [updatedExpansion] });

              // CRITICAL: Re-select the row to maintain visual selection and keep panel open
              // We need to do this after the transaction to ensure the row is updated first
              setTimeout(() => {
                this.gridApi.forEachNode(node => {
                  if (node.data?.id === updatedExpansion.id) {
                    node.setSelected(true, true); // true = selected, true = clear other selections
                  }
                });
              }, 0);
            }
          }
        });
      },
      error: (error) => {
        this.isAnalyzing.set(false);
        console.error('Error analyzing expansion value:', error);
        this.snackBar.open('Errore durante l\'analisi', 'Chiudi', { duration: 5000 });
      }
    });
  }

  analyzeAllValues() {
    this.isAnalyzingAll.set(true);
    this.expansionsService.analyzeAllValues().subscribe({
      next: (response: any) => {
        this.isAnalyzingAll.set(false);
        this.snackBar.open(`Analisi collettiva completata: ${response.data.successCount} completate, ${response.data.failedCount} fallite`, 'Chiudi', { duration: 5000 });
        this.loadExpansions();
      },
      error: (error) => {
        this.isAnalyzingAll.set(false);
        console.error('Error in bulk analytics:', error);
        this.snackBar.open('Errore nell\'analisi collettiva', 'Chiudi', { duration: 5000 });
      }
    });
  }

  // Grid State Management
  saveGridState(): void {
    if (!this.gridApi) return;

    const columnState = this.gridApi.getColumnState();
    const sortModel = this.gridApi.getColumnState().filter(col => col.sort != null);

    this.gridStateService.saveGridState(this.GRID_ID, {
      columnState,
      sortModel
    });

    this.snackBar.open('Grid configuration saved', 'Close', { duration: 3000 });
  }

  resetGridState(): void {
    if (!this.gridApi) return;

    this.gridStateService.clearGridState(this.GRID_ID);
    this.gridApi.resetColumnState();
    this.gridApi.sizeColumnsToFit();

    this.snackBar.open('Grid configuration reset to defaults', 'Close', { duration: 3000 });
  }

  onColumnMoved(): void {
    this.saveGridState();
  }

  onColumnVisible(): void {
    this.saveGridState();
  }

  onSortChanged(): void {
    this.saveGridState();
  }

  // Column Visibility Helper
  toggleColumnVisibility(colId: string): void {
    const col = this.gridApi.getColumn(colId);
    if (col) {
      this.gridApi.setColumnsVisible([colId], !col.isVisible());
      this.saveGridState();
    }
  }

  isColumnVisible(colId: string): boolean {
    const col = this.gridApi?.getColumn(colId);
    return col ? col.isVisible() : true;
  }

  getAllColumns(): any[] {
    return this.columnDefs;
  }
}
