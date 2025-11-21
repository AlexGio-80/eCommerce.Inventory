import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, ModuleRegistry, AllCommunityModule, GridApi } from 'ag-grid-community';
import { GamesService, Game, SyncResponse } from '../services/games.service';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
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

    :host ::ng-deep .ag-theme-material {
      --ag-header-background-color: #3f51b5;
      --ag-header-foreground-color: white;
    }
  `]
})
export class GamesPageComponent implements OnInit {
    games = signal<Game[]>([]);
    selectedGame = signal<Game | null>(null);
    isSyncingExpansions = signal(false);
    isSyncingAll = signal(false);

    private gridApi!: GridApi;

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

    constructor(
        private gamesService: GamesService,
        private snackBar: MatSnackBar
    ) { }

    ngOnInit() {
        this.loadGames();
    }

    onGridReady(params: any) {
        this.gridApi = params.api;
    }

    loadGames() {
        this.gamesService.getGames().subscribe({
            next: (data) => {
                this.games.set(data);
                console.log('Loaded games:', data);
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
            console.log('Selected game:', selectedRows[0]);
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
                // Revert toggle if needed, but since it's bound to selectedGame which hasn't changed yet, it might be tricky. 
                // Actually, the toggle in UI is bound to selectedGame().isEnabled.
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
                console.log('Sync expansions response:', response);
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
                console.log('Sync all response:', response);
                this.snackBar.open(response.message, 'Close', { duration: 5000 });
            },
            error: (error) => {
                this.isSyncingAll.set(false);
                console.error('Error performing full sync:', error);
                this.snackBar.open(`Error: ${error.error?.error || 'Failed to perform full sync'}`, 'Close', { duration: 5000 });
            }
        });
    }
}
