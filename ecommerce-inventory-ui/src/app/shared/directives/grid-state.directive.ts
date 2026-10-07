import { DestroyRef, Directive, Input, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AgGridAngular } from 'ag-grid-angular';
import { GridApi } from 'ag-grid-community';
import { GridStateService } from '../../core/services/grid-state.service';

/**
 * Salvataggio automatico delle colonne di una griglia AG Grid: ordine, larghezza, colonne
 * visibili, colonne bloccate e ordinamento. Si salva nel browser (localStorage) a ogni modifica
 * fatta dall'utente e si ripristina all'apertura.
 *
 * Uso: `<ag-grid-angular appGridState="id-univoco-della-griglia" ...>`.
 *
 * Salva solo le modifiche fatte a mano: quelle fatte dal codice (adattamento alla finestra,
 * ripristino, chiamate API) non sovrascrivono la configurazione salvata.
 */
@Directive({
  selector: 'ag-grid-angular[appGridState]',
  standalone: true
})
export class GridStateDirective implements OnInit {
  @Input({ required: true }) appGridState!: string;

  private static readonly userSources = new Set<string>([
    'uiColumnMoved', 'uiColumnResized', 'uiColumnDragged', 'uiColumnSorted',
    'contextMenu', 'columnMenu', 'toolPanelUi', 'toolPanelDragAndDrop'
  ]);

  private readonly grid = inject(AgGridAngular);
  private readonly gridState = inject(GridStateService);
  private readonly destroyRef = inject(DestroyRef);
  private api?: GridApi;

  ngOnInit(): void {
    this.grid.gridReady.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(e => {
      this.api = e.api;
      this.restore();
    });
    this.grid.columnMoved.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(e => { if (e.finished) this.saveIfUser(e.source); });
    this.grid.columnResized.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(e => { if (e.finished) this.saveIfUser(e.source); });
    this.grid.columnVisible.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(e => this.saveIfUser(e.source));
    this.grid.columnPinned.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(e => this.saveIfUser(e.source));
    this.grid.sortChanged.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(e => this.saveIfUser(e.source));
  }

  /** Torna alle colonne predefinite e dimentica la configurazione salvata. */
  reset(): void {
    this.gridState.clearGridState(this.appGridState);
    this.api?.resetColumnState();
  }

  private restore(): void {
    const saved = this.gridState.loadGridState(this.appGridState);
    if (saved?.columnState?.length) {
      this.api?.applyColumnState({ state: saved.columnState, applyOrder: true });
    }
  }

  private saveIfUser(source: string | undefined): void {
    if (!this.api || this.api.isDestroyed()) return;
    if (source && !GridStateDirective.userSources.has(source)) return;

    // Si conservano filtri e ricerca eventualmente salvati dalla pagina.
    const previous = this.gridState.loadGridState(this.appGridState);
    const columnState = this.api.getColumnState();
    this.gridState.saveGridState(this.appGridState, {
      ...previous,
      columnState,
      sortModel: columnState.filter(c => c.sort != null)
    });
  }
}
