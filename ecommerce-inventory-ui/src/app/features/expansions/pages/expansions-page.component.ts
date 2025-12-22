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
          <mat-card-title>{{ selectedExpansion()?.name }}</mat-card-title>
          <mat-card-subtitle>{{ selectedExpansion()?.gameName }} ({{ selectedExpansion()?.gameCode }})</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <div class="expansion-details">
            <div class="detail-row">
              <label>ID:</label>
              <span>{{ selectedExpansion()?.id }}</span>
            </div>
            <div class="detail-row">
              <label>Card Trader ID:</label>
              <span>{{ selectedExpansion()?.cardTraderId }}</span>
            </div>
            <div class="detail-row">
              <label>Code:</label>
              <span>{{ selectedExpansion()?.code }}</span>
            </div>
            <div class="detail-row" *ngIf="selectedExpansion()?.averageCardValue">
              <label>Valore Medio Carta:</label>
              <span class="value-highlight">€{{ selectedExpansion()?.averageCardValue?.toFixed(2) }}</span>
            </div>
            <div class="detail-row" *ngIf="selectedExpansion()?.totalMinPrice">
              <label>Valore Totale (Min):</label>
              <span class="value-highlight">€{{ selectedExpansion()?.totalMinPrice?.toFixed(2) }}</span>
            </div>
            <div class="detail-row" *ngIf="selectedExpansion()?.lastValueAnalysisUpdate">
              <label>Ultima Analisi:</label>
              <span>{{ selectedExpansion()?.lastValueAnalysisUpdate | date:'dd/MM/yyyy HH:mm' }}</span>
            </div>
          </div>
        </mat-card-content>
        <mat-card-actions>
          <button 
            mat-raised-button 
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
    }

    h1 {
      margin-bottom: 24px;
      color: #333;
    }

    .header-card {
      margin-bottom: 24px;
    }

    .expansion-details {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
      gap: 16px;
      margin-top: 16px;
    }

    .detail-row {
      display: flex;
      gap: 8px;
    }

    .detail-row label {
      font-weight: 500;
      color: #666;
    }

    .detail-row span {
      color: #333;
    }

    .value-highlight {
      font-weight: 500;
      color: #2e7d32 !important;
    }

    .header-actions {
      display: flex;
      gap: 8px;
      align-items: center;
    }

    mat-card-actions button {
      margin: 8px;
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
        this.loadExpansions(); // Refresh to get updated values
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
