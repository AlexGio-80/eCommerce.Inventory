import { Component, EventEmitter, Input, OnInit, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { debounceTime, distinctUntilChanged, filter, switchMap, tap, finalize } from 'rxjs/operators';
import { BlueprintsService } from '../../../core/services/blueprints.service';
import { Blueprint } from '../../../core/models';

@Component({
  selector: 'app-blueprint-selector',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatAutocompleteModule,
    MatInputModule,
    MatFormFieldModule,
    MatProgressSpinnerModule
  ],
  template: `
    <mat-form-field class="full-width" appearance="outline">
      <mat-label>Search Card (Blueprint)</mat-label>
      <input type="text"
             matInput
             [formControl]="searchControl"
             [matAutocomplete]="auto">
      <mat-spinner *ngIf="isLoading" matSuffix diameter="20"></mat-spinner>
      <mat-hint>Type at least 1 character to search (name, collector number, Italian name)</mat-hint>
      <mat-autocomplete #auto="matAutocomplete" [displayWith]="displayFn" (optionSelected)="onOptionSelected($event)">
        <mat-option *ngFor="let blueprint of filteredBlueprints" [value]="blueprint" class="blueprint-option-container">
          <div class="blueprint-option">
            <img [src]="blueprint.imageUrl || 'assets/placeholder-card.png'" class="option-image" alt="Card">
            <div class="option-details">
              <span class="name">{{ blueprint.name }}</span>
              <span class="details">
                {{ blueprint.expansion?.name }} ({{ blueprint.game?.name }})
              </span>
              <span class="italian-name" *ngIf="blueprint.italianName">{{ blueprint.italianName }}</span>
            </div>
          </div>
        </mat-option>
      </mat-autocomplete>
    </mat-form-field>
  `,
  styles: [`
    .full-width {
      width: 100%;
    }
    .blueprint-option {
      display: flex;
      align-items: center;
      gap: 16px;
      padding: 8px 0;
    }
    .option-image {
      width: 50px;
      height: 70px;
      object-fit: cover;
      border-radius: 4px;
      box-shadow: 0 2px 4px rgba(0,0,0,0.15);
    }
    .option-details {
      display: flex;
      flex-direction: column;
      line-height: 1.3;
    }
    .name {
      font-weight: 500;
      font-size: 1rem;
    }
    .italian-name {
      font-size: 0.85em;
      color: #3f51b5;
      font-style: italic;
      margin-top: 2px;
    }
    .details {
      font-size: 0.85em;
      color: #666;
    }
    /* Override material option height to fit larger image */
    ::ng-deep .blueprint-option-container {
      height: auto !important;
      min-height: 96px;
    }
    /* Make autocomplete panel wider to accommodate larger images */
    ::ng-deep .mat-mdc-autocomplete-panel {
      min-width: 400px;
    }
  `]
})
export class BlueprintSelectorComponent implements OnInit {
  @Output() selectionChange = new EventEmitter<Blueprint>();

  searchControl = new FormControl('');
  filteredBlueprints: Blueprint[] = [];
  isLoading = false;

  constructor(private blueprintsService: BlueprintsService) { }

  ngOnInit() {
    this.searchControl.valueChanges.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      filter(value => typeof value === 'string' && value.length >= 1),
      tap(() => this.isLoading = true),
      switchMap(value => this.blueprintsService.searchBlueprints(value as string)
        .pipe(
          finalize(() => this.isLoading = false)
        )
      )
    ).subscribe(blueprints => {
      this.filteredBlueprints = blueprints;
    });
  }

  displayFn(blueprint: Blueprint): string {
    return blueprint && blueprint.name ? blueprint.name : '';
  }

  onOptionSelected(event: any) {
    this.selectionChange.emit(event.option.value);
  }
}
