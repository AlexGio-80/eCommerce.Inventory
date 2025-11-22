import { Component, OnInit, signal, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, ModuleRegistry, AllCommunityModule, GridApi, GridReadyEvent } from 'ag-grid-community';
import { GamesService, Game } from '../services/games.service';
import { GridStateService } from '../../../core/services/grid-state.service';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatMenuModule } from '@angular/material/menu';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatTooltipModule } from '@angular/material/tooltip';
import { FormsModule } from '@angular/forms';

// Register AG Grid modules
ModuleRegistry.registerModules([AllCommunityModule]);

@Component({
    selector: 'app-games-page',
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
        MatSlideToggleModule,
        MatMenuModule,
        MatIconModule,
        MatCheckboxModule,
        MatTooltipModule,
        FormsModule
    ],
    templateUrl: './games-page.component.html',
    styles: [`
    .games-container {
      padding: 24px;
    }

    h1 {
      margin-bottom: 24px;
      color: #333;
    }

    .header-card {
      margin-bottom: 24px;
    }

    .game-details {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
      gap: 16px;
      margin-top: 16px;
    }

    .detail-row {
      display: flex;
      gap: 8px;
      align-items: center;
    }

    .detail-row label {
      font-weight: 500;
      color: #666;
    }

    .detail-row span {
      color: #333;
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

    ::ng-deep .mat-mdc-menu-content {
      padding: 0 !important;
    }
  `]
})
export class GamesPageComponent implements OnInit {
    games = signal<Game[]>([]);
    selectedGame = signal<Game | null>(null);
    isSyncingExpansions = signal(false);
    isSyncingAll = signal(false);

    private gridApi!: GridApi;
    private readonly GRID_ID = 'games-grid';

    columnDefs: ColDef[] = [
        { field: 'id', headerName: 'ID', width: 80, filter: 'agNumberColumnFilter' },
        { field: 'cardTraderId', headerName: 'CT ID', width: 100, filter: 'agNumberColumnFilter' },
        { field: 'name', headerName: 'Name', flex: 2, filter: 'agTextColumnFilter' },
        { field: 'code', headerName: 'Code', width: 120, filter: 'agTextColumnFilter' },
        {
            field: 'isEnabled',
            headerName: 'Enabled',
            width: 120,
            cellRenderer: (params: any) => {
                return `<input type="checkbox" ${params.value ? 'checked' : ''} disabled />`;
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
        private gamesService: GamesService,
        private gridStateService: GridStateService,
        private snackBar: MatSnackBar
    ) { }

    ngOnInit() {
        this.loadGames();
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

    loadGames() {
        this.gamesService.getGames().subscribe({
            next: (data) => {
                this.games.set(data);
            },
            error: (error) => {
                console.error('Error loading games:', error);
                this.snackBar.open('Error loading games', 'Close', { duration: 3000 });
            }
        });
    }

    onSelectionChanged(event: any) {
        const selectedRows = event.api.getSelectedRows();
        if (selectedRows.length > 0) {
            this.selectedGame.set(selectedRows[0]);
        } else {
            this.selectedGame.set(null);
        }
    }

    toggleGameEnabled(enabled: boolean) {
        const game = this.selectedGame();
        if (!game) return;

        this.gamesService.updateGame(game.id, enabled).subscribe({
            next: (updatedGame) => {
                this.selectedGame.set(updatedGame);
                // Update list
                const currentGames = this.games();
                const index = currentGames.findIndex(g => g.id === updatedGame.id);
                if (index !== -1) {
                    currentGames[index] = updatedGame;
                    this.games.set([...currentGames]);
                    this.gridApi.setGridOption('rowData', currentGames);
                }
                this.snackBar.open(`Game ${updatedGame.name} ${updatedGame.isEnabled ? 'enabled' : 'disabled'}`, 'Close', { duration: 3000 });
            },
            error: (error) => {
                console.error('Error updating game:', error);
                this.snackBar.open('Error updating game status', 'Close', { duration: 3000 });
            }
        });
    }

    syncExpansions() {
        const game = this.selectedGame();
        if (!game) return;

        this.isSyncingExpansions.set(true);
        this.snackBar.open('Starting expansions sync...', 'Close', { duration: 2000 });

        this.gamesService.syncExpansions(game.id).subscribe({
            next: (response) => {
                this.isSyncingExpansions.set(false);
                this.snackBar.open(response.message, 'Close', { duration: 5000 });
            },
            error: (error) => {
                this.isSyncingExpansions.set(false);
                console.error('Error syncing expansions:', error);
                this.snackBar.open(`Error: ${error.error?.error || 'Failed to sync expansions'}`, 'Close', { duration: 5000 });
            }
        });
    }

    syncAll() {
        const game = this.selectedGame();
        if (!game) return;

        this.isSyncingAll.set(true);
        this.snackBar.open('Starting full sync (Expansions + Blueprints)...', 'Close', { duration: 2000 });

        this.gamesService.syncAll(game.id).subscribe({
            next: (response) => {
                this.isSyncingAll.set(false);
                this.snackBar.open(response.message, 'Close', { duration: 5000 });
            },
            error: (error) => {
                this.isSyncingAll.set(false);
                console.error('Error performing full sync:', error);
                this.snackBar.open(`Error: ${error.error?.error || 'Failed to perform full sync'}`, 'Close', { duration: 5000 });
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
