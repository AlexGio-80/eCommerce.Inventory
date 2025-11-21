import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatTableModule } from '@angular/material/table';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { Router } from '@angular/router';
import { BlueprintSelectorComponent } from '../../../../shared/components/blueprint-selector/blueprint-selector.component';
import { ProductsService } from '../../services/products.service';
import { PendingListingsService, PendingListing, CreatePendingListingDto } from '../../services/pending-listings.service';
import { Blueprint } from '../../../../core/models';

@Component({
  selector: 'app-create-listing',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatCheckboxModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    MatTooltipModule,
    MatTableModule,
    MatIconModule,
    MatButtonToggleModule,
    BlueprintSelectorComponent
  ],
  templateUrl: './create-listing.component.html',
  styles: [`
    .create-listing-container {
      padding: 24px;
      max-width: 1200px; /* Increased max-width for side-by-side layout */
      margin: 0 auto;
    }

    mat-card-header {
      display: flex;
      align-items: center;
    }

    .form-row {
      display: flex;
      gap: 16px;
      margin-bottom: 8px;
    }

    .form-row > * {
      flex: 1;
    }

    .full-width {
      width: 100%;
    }

    .checkbox-row {
      display: flex;
      gap: 24px;
      margin: 16px 0;
    }

    .selected-blueprint-info {
      margin-bottom: 24px;
      padding: 16px;
      background-color: #f5f5f5;
      border-radius: 4px;
      display: flex;
      gap: 16px;
      align-items: center;
    }

    .blueprint-image {
      width: 60px;
      height: 85px;
      object-fit: cover;
      border-radius: 4px;
    }

    .blueprint-details h3 {
      margin: 0 0 4px 0;
    }

    .blueprint-details p {
      margin: 0;
      color: #666;
    }
  `]
})
export class CreateListingComponent {
  listingForm: FormGroup;
  selectedBlueprint = signal<Blueprint | null>(null);
  isSubmitting = signal(false);
  saveDefaults = signal(false);
  editingId = signal<number | null>(null);

  // Pending Listings
  pendingListings = signal<PendingListing[]>([]);
  displayedColumns: string[] = ['image', 'name', 'condition', 'quantity', 'price', 'status', 'actions'];
  filterStatus = signal<'all' | 'synced' | 'unsynced' | 'error'>('unsynced');
  isSyncing = signal(false);

  private readonly STORAGE_KEY = 'listing_defaults';

  conditions = ['Mint', 'Near Mint', 'Excellent', 'Good', 'Light Played', 'Played', 'Poor'];
  languages = ['English', 'Italian', 'Japanese', 'French', 'German', 'Spanish', 'Chinese'];

  constructor(
    private fb: FormBuilder,
    private productsService: ProductsService,
    private pendingListingsService: PendingListingsService,
    private snackBar: MatSnackBar,
    private router: Router
  ) {
    const defaults = this.loadDefaults();
    this.listingForm = this.fb.group({
      quantity: [1, [Validators.required, Validators.min(1)]],
      sellingPrice: [null, [Validators.required, Validators.min(0)]],
      condition: [defaults.condition, Validators.required],
      language: [defaults.language, Validators.required],
      isFoil: [defaults.isFoil],
      isSigned: [defaults.isSigned],
      location: [''],
      tag: [''],
      purchasePrice: [0, [Validators.required, Validators.min(0)]]
    });
    this.saveDefaults.set(defaults.saveEnabled);
    this.loadPendingListings();
  }

  private loadDefaults() {
    const saved = localStorage.getItem(this.STORAGE_KEY);
    if (saved) {
      try {
        return JSON.parse(saved);
      } catch {
        return this.getDefaultValues();
      }
    }
    return this.getDefaultValues();
  }

  private getDefaultValues() {
    return {
      condition: 'Near Mint',
      language: 'English',
      isFoil: false,
      isSigned: false,
      sellingPrice: null,
      purchasePrice: 0,
      saveEnabled: false
    };
  }

  onToggleSaveDefaults() {
    const newValue = !this.saveDefaults();
    this.saveDefaults.set(newValue);

    if (newValue) {
      this.saveCurrentDefaults();
    } else {
      localStorage.removeItem(this.STORAGE_KEY);
    }
  }

  private saveCurrentDefaults() {
    const defaults = {
      condition: this.listingForm.get('condition')?.value,
      language: this.listingForm.get('language')?.value,
      isFoil: this.listingForm.get('isFoil')?.value,
      isSigned: this.listingForm.get('isSigned')?.value,
      sellingPrice: this.listingForm.get('sellingPrice')?.value,
      purchasePrice: this.listingForm.get('purchasePrice')?.value,
      saveEnabled: true
    };
    localStorage.setItem(this.STORAGE_KEY, JSON.stringify(defaults));
  }

  onBlueprintSelected(blueprint: Blueprint) {
    this.selectedBlueprint.set(blueprint);
  }

  loadPendingListings() {
    const status = this.filterStatus();
    let isSynced: boolean | undefined;
    let hasError: boolean | undefined;

    if (status === 'synced') isSynced = true;
    if (status === 'unsynced') isSynced = false;
    if (status === 'error') hasError = true;

    this.pendingListingsService.getPendingListings(1, 100, isSynced, hasError).subscribe({
      next: (response) => {
        this.pendingListings.set(response.items);
      },
      error: (error) => console.error('Error loading pending listings', error)
    });
  }

  onFilterChange(value: string) {
    this.filterStatus.set(value as any);
    this.loadPendingListings();
  }

  onResetForm() {
    this.selectedBlueprint.set(null);
    this.editingId.set(null);

    if (this.saveDefaults()) {
      const defaults = this.loadDefaults();
      this.listingForm.patchValue({
        quantity: 1,
        sellingPrice: defaults.sellingPrice,
        purchasePrice: defaults.purchasePrice,
        condition: defaults.condition,
        language: defaults.language,
        isFoil: defaults.isFoil,
        isSigned: defaults.isSigned,
        location: '',
        tag: ''
      });
    } else {
      this.listingForm.reset({
        quantity: 1,
        sellingPrice: null,
        purchasePrice: 0,
        condition: 'Near Mint',
        language: 'English',
        isFoil: false,
        isSigned: false,
        location: '',
        tag: ''
      });
    }
  }

  onEditPending(item: PendingListing) {
    if (item.isSynced) return;

    this.editingId.set(item.id);
    this.selectedBlueprint.set(item.blueprint || null);

    this.listingForm.patchValue({
      condition: item.condition,
      language: item.language,
      quantity: item.quantity,
      sellingPrice: item.sellingPrice,
      purchasePrice: item.purchasePrice || 0,
      location: item.location,
      tag: item.tag,
      isFoil: item.isFoil,
      isSigned: item.isSigned
    });
  }

  onCancelEdit() {
    this.editingId.set(null);
    this.onResetForm();
  }

  onDeletePending(id: number) {
    if (confirm('Are you sure you want to delete this pending listing?')) {
      this.pendingListingsService.deletePendingListing(id).subscribe({
        next: () => {
          this.snackBar.open('Listing removed from queue', 'Close', { duration: 3000 });
          this.loadPendingListings();
          if (this.editingId() === id) {
            this.onCancelEdit();
          }
        },
        error: () => this.snackBar.open('Failed to delete listing', 'Close', { duration: 3000 })
      });
    }
  }

  onSyncAll() {
    this.isSyncing.set(true);
    this.pendingListingsService.syncPendingListings().subscribe({
      next: (result) => {
        this.isSyncing.set(false);
        const message = `Synced: ${result.success}, Errors: ${result.errors}`;
        this.snackBar.open(message, 'Close', { duration: 5000 });
        this.loadPendingListings();
      },
      error: (error) => {
        this.isSyncing.set(false);
        this.snackBar.open('Sync failed', 'Close', { duration: 5000 });
      }
    });
  }

  onSubmit() {
    if (this.listingForm.invalid || !this.selectedBlueprint()) {
      return;
    }

    this.isSubmitting.set(true);
    const formValue = this.listingForm.value;

    if (this.saveDefaults()) {
      this.saveCurrentDefaults();
    }

    const dto: CreatePendingListingDto = {
      blueprintId: this.selectedBlueprint()!.id,
      quantity: formValue.quantity,
      price: formValue.sellingPrice, // Map sellingPrice to price
      condition: formValue.condition,
      language: formValue.language,
      isFoil: formValue.isFoil,
      isSigned: formValue.isSigned,
      location: formValue.location,
      tag: formValue.tag,
      purchasePrice: formValue.purchasePrice
    };

    if (this.editingId()) {
      // Update existing
      this.pendingListingsService.updatePendingListing(this.editingId()!, dto).subscribe({
        next: (updated) => {
          this.snackBar.open('Listing updated', 'Close', { duration: 3000 });
          this.isSubmitting.set(false);
          this.editingId.set(null);
          this.loadPendingListings();

          // Logic for "Save Defaults" OFF: Reset fields, keep blueprint
          if (!this.saveDefaults()) {
            this.listingForm.reset({
              quantity: 1,
              sellingPrice: null,
              purchasePrice: 0,
              condition: 'Near Mint',
              language: 'English',
              isFoil: false,
              isSigned: false,
              location: '',
              tag: ''
            });
          }
        },
        error: (err) => {
          console.error('Error updating listing', err);
          this.snackBar.open(err.error?.message || 'Error updating listing', 'Close', { duration: 3000 });
          this.isSubmitting.set(false);
        }
      });
    } else {
      // Create new
      this.pendingListingsService.createPendingListing(dto).subscribe({
        next: (item) => {
          this.isSubmitting.set(false);
          this.snackBar.open('Added to queue', 'Undo', { duration: 3000 })
            .onAction().subscribe(() => {
              this.onDeletePending(item.id);
            });

          this.loadPendingListings();

          // Logic for "Save Defaults" OFF: Reset fields, keep blueprint
          if (!this.saveDefaults()) {
            this.listingForm.reset({
              quantity: 1,
              sellingPrice: null,
              purchasePrice: 0,
              condition: 'Near Mint',
              language: 'English',
              isFoil: false,
              isSigned: false,
              location: '',
              tag: ''
            });
          }
        },
        error: (error) => {
          this.isSubmitting.set(false);
          console.error('Error creating pending listing:', error);
          if (error.status === 409) {
            this.snackBar.open('Duplicate listing already in queue', 'Close', { duration: 5000 });
          } else {
            this.snackBar.open('Failed to add to queue', 'Close', { duration: 5000 });
          }
        }
      });
    }
  }
}
