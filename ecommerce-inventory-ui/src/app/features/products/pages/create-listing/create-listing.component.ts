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
import { Router } from '@angular/router';
import { BlueprintSelectorComponent } from '../../../../shared/components/blueprint-selector/blueprint-selector.component';
import { ProductsService, CreateInventoryItemDto } from '../../services/products.service';
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
    BlueprintSelectorComponent
  ],
  templateUrl: './create-listing.component.html',
  styles: [`
    .create-listing-container {
      padding: 24px;
      max-width: 800px;
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

  private readonly STORAGE_KEY = 'listing_defaults';

  conditions = ['Mint', 'Near Mint', 'Excellent', 'Good', 'Light Played', 'Played', 'Poor'];
  languages = ['English', 'Italian', 'Japanese', 'French', 'German', 'Spanish', 'Chinese'];

  constructor(
    private fb: FormBuilder,
    private productsService: ProductsService,
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
      saveEnabled: true
    };
    localStorage.setItem(this.STORAGE_KEY, JSON.stringify(defaults));
  }

  onBlueprintSelected(blueprint: Blueprint) {
    this.selectedBlueprint.set(blueprint);
  }

  onSubmit() {
    if (this.listingForm.invalid || !this.selectedBlueprint()) {
      return;
    }

    this.isSubmitting.set(true);
    const formValue = this.listingForm.value;

    const dto: CreateInventoryItemDto = {
      blueprintId: this.selectedBlueprint()!.id,
      quantity: formValue.quantity,
      price: formValue.sellingPrice,
      condition: formValue.condition,
      language: formValue.language,
      isFoil: formValue.isFoil,
      isSigned: formValue.isSigned,
      location: formValue.location,
      tag: formValue.tag,
      purchasePrice: formValue.purchasePrice
    };

    this.productsService.createProduct(dto).subscribe({
      next: (item) => {
        this.isSubmitting.set(false);
        this.snackBar.open('Product listing created successfully', 'View Inventory', { duration: 5000 })
          .onAction().subscribe(() => {
            this.router.navigate(['/layout/inventory']);
          });

        // Save defaults if toggle is on
        if (this.saveDefaults()) {
          this.saveCurrentDefaults();
        }

        // Reset form but keep saved defaults
        const defaults = this.loadDefaults();
        this.listingForm.reset({
          quantity: 1,
          sellingPrice: null,
          condition: defaults.condition,
          language: defaults.language,
          isFoil: defaults.isFoil,
          isSigned: defaults.isSigned,
          location: '',
          tag: '',
          purchasePrice: 0
        });
        this.selectedBlueprint.set(null);
      },
      error: (error) => {
        this.isSubmitting.set(false);
        console.error('Error creating product:', error);
        this.snackBar.open('Failed to create product listing', 'Close', { duration: 5000 });
      }
    });
  }
}
