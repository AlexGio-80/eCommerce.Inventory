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
    <mat-form-field class="full-width">
      <mat-label>Search Card (Blueprint)</mat-label>
      <input type="text"
             placeholder="Type card name (e.g. 'Sol Ring Commander')..."
             matInput
             [formControl]="searchControl"
             [matAutocomplete]="auto">
      <mat-spinner *ngIf="isLoading" matSuffix diameter="20"></mat-spinner>
      <mat-autocomplete #auto="matAutocomplete" [displayWith]="displayFn" (optionSelected)="onOptionSelected($event)">
        <mat-option *ngFor="let blueprint of filteredBlueprints" [value]="blueprint" class="blueprint-option-container">
          <div class="blueprint-option">
            <img [src]="blueprint.imageUrl || 'assets/placeholder-card.png'" class="option-image" alt="Card">
            <div class="option-details">
              <span class="name">{{ blueprint.name }}</span>
              <span class="details">
                {{ blueprint.expansion?.name }} ({{ blueprint.game?.name }})
              </span>
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
      gap: 12px;
      padding: 4px 0;
    }
    .option-image {
      width: 30px;
      height: 42px;
      object-fit: cover;
      border-radius: 2px;
    }
    .option-details {
      display: flex;
      flex-direction: column;
      line-height: 1.2;
    }
    .name {
      font-weight: 500;
    }
    .details {
      font-size: 0.8em;
      color: #666;
    }
    /* Override material option height to fit image */
    ::ng-deep .blueprint-option-container {
      height: auto !important;
      min-height: 56px;
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
      filter(value => typeof value === 'string' && value.length >= 2),
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
