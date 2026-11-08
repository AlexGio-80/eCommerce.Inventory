import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { CardTraderApiService } from '../../../../core/services/cardtrader-api.service';

/**
 * Lotto di carte venduto fuori da Card Trader (es. 100 bulk su Subito): sito, data, incasso e, per ogni Tag, quante
 * carte. Diventa un ordine pagato, così conta nei report e nel "Venduto" del registro acquisti come una vendita
 * Card Trader. L'incasso si divide in parti uguali sulle carte e non paga commissione.
 */
@Component({
    selector: 'app-external-lot-dialog',
    standalone: true,
    imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatIconModule],
    template: `
        <h2 mat-dialog-title>Lotto venduto fuori da Card Trader</h2>
        <mat-dialog-content>
            <p class="hint">Le carte le togli da Card Trader come sempre; qui registri l'incasso, che conta come una vendita
                nei report e nel "Venduto" del registro acquisti, diviso in parti uguali sulle carte e senza commissione.</p>
            <div class="row">
                <mat-form-field appearance="outline"><mat-label>Sito</mat-label><input matInput [(ngModel)]="site"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Data</mat-label><input matInput type="date" [(ngModel)]="date"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Incasso €</mat-label>
                    <input matInput type="number" min="0" step="0.01" [(ngModel)]="total"></mat-form-field>
            </div>
            @for (l of lines; track $index) {
                <div class="row">
                    <mat-form-field appearance="outline" class="tag"><mat-label>Tag delle carte</mat-label>
                        <input matInput placeholder="#FRA_OLD" [(ngModel)]="l.tag"></mat-form-field>
                    <mat-form-field appearance="outline"><mat-label>Carte</mat-label>
                        <input matInput type="number" min="1" step="1" [(ngModel)]="l.quantity"></mat-form-field>
                    @if (lines.length > 1) {
                        <button mat-icon-button (click)="lines.splice($index, 1)" title="Togli il Tag"><mat-icon>close</mat-icon></button>
                    }
                </div>
            }
            <button mat-button (click)="lines.push({ tag: '', quantity: null })"><mat-icon>add</mat-icon> Altro Tag</button>
            <mat-form-field appearance="outline" class="full"><mat-label>Note (facoltative)</mat-label>
                <input matInput placeholder="Lotto 100 bulk FRA" [(ngModel)]="notes"></mat-form-field>
            @if (cards() > 0 && (total ?? 0) > 0) {
                <p class="hint">{{ cards() }} carte, circa {{ (total! / cards()).toFixed(3) }} € a carta.</p>
            }
            @if (error) { <p class="error">{{ error }}</p> }
        </mat-dialog-content>
        <mat-dialog-actions align="end">
            <button mat-button mat-dialog-close>Annulla</button>
            <button mat-raised-button color="primary" [disabled]="saving || !valid()" (click)="save()">
                {{ saving ? 'Registro…' : 'Registra il lotto' }}</button>
        </mat-dialog-actions>
    `,
    styles: [`
        .row { display: flex; gap: 8px; align-items: center; }
        .row mat-form-field { flex: 1; }
        .row .tag { flex: 2; }
        .full { width: 100%; }
        .hint { color: #616161; font-size: 12px; margin: 0 0 12px; }
        .error { color: #c62828; }
    `]
})
export class ExternalLotDialogComponent {
    site = 'Subito';
    date = new Date().toISOString().slice(0, 10);
    total: number | null = null;
    notes = '';
    lines: { tag: string; quantity: number | null }[] = [{ tag: '', quantity: null }];
    saving = false;
    error = '';

    constructor(private api: CardTraderApiService, private dialogRef: MatDialogRef<ExternalLotDialogComponent>) {}

    cards(): number {
        return this.lines.reduce((t, l) => t + (l.tag.trim() && (l.quantity ?? 0) > 0 ? l.quantity! : 0), 0);
    }

    valid(): boolean {
        return !!this.site.trim() && !!this.date && (this.total ?? 0) > 0 && this.cards() > 0;
    }

    save(): void {
        this.saving = true;
        this.error = '';
        this.api.registerExternalLot({
            site: this.site.trim(),
            date: this.date,
            total: this.total!,
            notes: this.notes.trim() || null,
            lines: this.lines.filter(l => l.tag.trim() && (l.quantity ?? 0) > 0).map(l => ({ tag: l.tag.trim(), quantity: l.quantity! }))
        }).subscribe({
            next: res => this.dialogRef.close(res.data),
            error: err => {
                this.saving = false;
                this.error = err?.error?.message ?? 'Registrazione non riuscita.';
            }
        });
    }
}
