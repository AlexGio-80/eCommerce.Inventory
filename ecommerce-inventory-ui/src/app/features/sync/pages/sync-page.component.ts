import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatListModule } from '@angular/material/list';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CardTraderApiService } from '../../../core/services';

interface SyncEntity {
  key: string;
  label: string;
  icon: string;
  selected: boolean;
  progress: number;
  status: 'pending' | 'syncing' | 'success' | 'error';
  message: string;
}

interface SyncLog {
  timestamp: Date;
  message: string;
  type: 'info' | 'success' | 'warning' | 'error';
}

@Component({
  selector: 'app-sync-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatCheckboxModule,
    MatProgressBarModule,
    MatListModule,
    MatChipsModule,
    MatDividerModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './sync-page.component.html',
  styleUrls: ['./sync-page.component.scss'],
})
export class SyncPageComponent implements OnInit {
  // Entities to sync
  entities = signal<SyncEntity[]>([
    { key: 'games', label: 'Giochi', icon: 'games', selected: true, progress: 0, status: 'pending', message: '' },
    { key: 'categories', label: 'Categorie (Proprietà)', icon: 'category', selected: true, progress: 0, status: 'pending', message: '' },
    { key: 'expansions', label: 'Espansioni', icon: 'extension', selected: true, progress: 0, status: 'pending', message: '' },
    { key: 'blueprints', label: 'Blueprints (Carte)', icon: 'dashboard', selected: true, progress: 0, status: 'pending', message: '' },
    { key: 'properties', label: 'Proprietà Aggiuntive', icon: 'tune', selected: false, progress: 0, status: 'pending', message: '' },
  ]);

  // Sync state
  isSyncing = signal(false);
  syncLogs = signal<SyncLog[]>([]);
  lastSyncTime = signal<Date | null>(null);
  syncProgress = signal(0);
  syncStats = signal<{ added: number; updated: number; failed: number }>({ added: 0, updated: 0, failed: 0 });

  constructor(private apiService: CardTraderApiService) {}

  ngOnInit(): void {
    this.loadSyncHistory();
  }

  toggleEntity(key: string): void {
    const entities = this.entities();
    const entity = entities.find((e) => e.key === key);
    if (entity) {
      entity.selected = !entity.selected;
      this.entities.set([...entities]);
    }
  }

  toggleAllEntities(selectAll: boolean): void {
    const entities = this.entities().map((e) => ({
      ...e,
      selected: selectAll,
    }));
    this.entities.set(entities);
  }

  getSelectedCount(): number {
    return this.entities().filter((e) => e.selected).length;
  }

  async startSync(): Promise<void> {
    if (this.isSyncing()) {
      return;
    }

    const selectedEntities = this.entities().filter((e) => e.selected);
    if (selectedEntities.length === 0) {
      this.addLog('Nessuna entità selezionata per la sincronizzazione', 'warning');
      return;
    }

    this.isSyncing.set(true);
    this.syncLogs.set([]);
    this.syncStats.set({ added: 0, updated: 0, failed: 0 });
    this.addLog(`Inizio sincronizzazione di ${selectedEntities.length} entità`, 'info');

    // Reset progress
    const entities = this.entities().map((e) => ({
      ...e,
      progress: 0,
      status: e.selected ? ('syncing' as const) : ('pending' as const),
      message: e.selected ? 'In sincronizzazione...' : '',
    }));
    this.entities.set(entities);

    try {
      // Build the sync request with selected entities
      // Note: 'categories' key maps to 'syncCategories' (includes category properties)
      // Note: 'properties' key currently not used (reserved for future expansion)
      const syncRequest = {
        syncGames: selectedEntities.some((e) => e.key === 'games'),
        syncCategories: selectedEntities.some((e) => e.key === 'categories'), // Syncs categories with their properties/values
        syncExpansions: selectedEntities.some((e) => e.key === 'expansions'),
        syncBlueprints: selectedEntities.some((e) => e.key === 'blueprints'),
        syncProperties: selectedEntities.some((e) => e.key === 'properties'),
      };

      // Call the sync API endpoint
      this.apiService.syncCardTraderData(syncRequest).subscribe({
        next: (response) => {
          this.handleSyncSuccess(response, selectedEntities);
        },
        error: (error) => {
          this.handleSyncError(error, selectedEntities);
        },
      });
    } catch (error) {
      this.addLog(`Errore durante la sincronizzazione: ${error}`, 'error');
      this.isSyncing.set(false);
    }
  }

  private handleSyncSuccess(response: any, selectedEntities: SyncEntity[]): void {
    // Update each entity with success
    const selectedKeys = selectedEntities.map((e) => e.key);
    const updatedEntities = this.entities().map((entity) => {
      if (selectedKeys.includes(entity.key)) {
        return {
          ...entity,
          status: 'success' as const,
          progress: 100,
          message: `Sincronizzato con successo`,
        };
      }
      return entity;
    });
    this.entities.set(updatedEntities);

    // Parse response data (assuming response has data structure)
    const data = response.data || {};
    const stats = {
      added: data.added || 0,
      updated: data.updated || 0,
      failed: data.failed || 0,
    };
    this.syncStats.set(stats);

    this.lastSyncTime.set(new Date());
    // Save to local storage for persistence
    localStorage.setItem('lastSyncTime', new Date().toISOString());

    this.syncProgress.set(100);

    this.addLog(
      `Sincronizzazione completata: ${stats.added} aggiunti, ${stats.updated} aggiornati, ${stats.failed} falliti`,
      'success'
    );

    this.isSyncing.set(false);

    // Auto-hide success messages after 5 seconds
    setTimeout(() => {
      const resetEntities = this.entities().map((entity) => {
        if (selectedKeys.includes(entity.key) && entity.status === 'success') {
          return {
            ...entity,
            status: 'pending' as const,
            message: '',
            progress: 0,
          };
        }
        return entity;
      });
      this.entities.set(resetEntities);
    }, 5000);
  }

  private handleSyncError(error: any, selectedEntities: SyncEntity[]): void {
    const selectedKeys = selectedEntities.map((e) => e.key);
    const entities = this.entities().map((entity) => {
      if (selectedKeys.includes(entity.key)) {
        return {
          ...entity,
          status: 'error' as const,
          message: error.error?.message || 'Errore sconosciuto',
        };
      }
      return entity;
    });
    this.entities.set(entities);

    this.addLog(`Errore di sincronizzazione: ${error.error?.message || error.message}`, 'error');
    this.isSyncing.set(false);
  }

  addLog(message: string, type: 'info' | 'success' | 'warning' | 'error' = 'info'): void {
    const logs = this.syncLogs();
    logs.push({
      timestamp: new Date(),
      message,
      type,
    });
    this.syncLogs.set(logs);

    // Keep only last 50 logs
    if (logs.length > 50) {
      logs.shift();
      this.syncLogs.set(logs);
    }
  }

  private loadSyncHistory(): void {
    // TODO: Load last sync time from local storage or backend
    const lastSync = localStorage.getItem('lastSyncTime');
    if (lastSync) {
      this.lastSyncTime.set(new Date(lastSync));
    }
  }

  clearLogs(): void {
    this.syncLogs.set([]);
  }

  getStatusIcon(status: string): string {
    const iconMap: { [key: string]: string } = {
      pending: 'schedule',
      syncing: 'hourglass_empty',
      success: 'check_circle',
      error: 'error',
    };
    return iconMap[status] || 'schedule';
  }

  getStatusColor(status: string): string {
    const colorMap: { [key: string]: string } = {
      pending: 'warn',
      syncing: 'primary',
      success: 'accent',
      error: 'warn',
    };
    return colorMap[status] || 'primary';
  }

  getLogChipColor(type: string): string {
    const colorMap: { [key: string]: string } = {
      info: 'primary',
      success: 'accent',
      warning: 'warn',
      error: 'warn',
    };
    return colorMap[type] || 'primary';
  }
}
