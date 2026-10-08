import { DestroyRef, Directive, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AgGridAngular } from 'ag-grid-angular';
import { CellDoubleClickedEvent } from 'ag-grid-community';

/**
 * Doppio clic su una cella di una griglia AG Grid: ne copia il valore negli appunti, così come
 * appare a schermo (con la formattazione della colonna). Serve a prendere un Tag o il nome di un
 * prodotto senza dover aprire la modifica.
 *
 * Si aggancia da sola a ogni `<ag-grid-angular>` del componente che la importa.
 */
@Directive({
  selector: 'ag-grid-angular',
  standalone: true
})
export class GridCellCopyDirective implements OnInit {
  private readonly grid = inject(AgGridAngular);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    this.grid.cellDoubleClicked.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(e => this.copy(e));
  }

  private async copy(e: CellDoubleClickedEvent): Promise<void> {
    if (!e.node || e.colDef.editable) return;

    const value = e.api.getCellValue({ rowNode: e.node, colKey: e.column, useFormatter: true });
    if (value == null || typeof value === 'object') return;

    const text = String(value).trim();
    if (!text) return;

    if (await writeToClipboard(text)) {
      const shown = text.length > 60 ? text.substring(0, 57) + '...' : text;
      this.snackBar.open(`Copiato: ${shown}`, undefined, { duration: 1500 });
    } else {
      this.snackBar.open('Impossibile copiare negli appunti', undefined, { duration: 2500 });
    }
  }
}

/**
 * `navigator.clipboard` esiste solo nelle origini sicure (https o localhost): su
 * `http://inventory.local` non c'è, e si ripiega sulla copia da una casella di testo nascosta.
 */
async function writeToClipboard(text: string): Promise<boolean> {
  if (navigator.clipboard && window.isSecureContext) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // si prova il ripiego
    }
  }

  const textarea = document.createElement('textarea');
  textarea.value = text;
  textarea.setAttribute('readonly', '');
  textarea.style.position = 'fixed';
  textarea.style.opacity = '0';
  document.body.appendChild(textarea);
  textarea.select();
  try {
    return document.execCommand('copy');
  } catch {
    return false;
  } finally {
    document.body.removeChild(textarea);
  }
}
