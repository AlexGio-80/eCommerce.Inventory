import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subject, Subscription, debounceTime, distinctUntilChanged } from 'rxjs';
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
import { MatChipsModule } from '@angular/material/chips';
import { CardTraderApiService } from '../../../../core/services/cardtrader-api.service';
import { Router } from '@angular/router';
import { BlueprintSelectorComponent } from '../../../../shared/components/blueprint-selector/blueprint-selector.component';
import { ProductsService } from '../../services/products.service';
import { PendingListingsService, PendingListing, CreatePendingListingDto } from '../../services/pending-listings.service';
import { Blueprint } from '../../../../core/models';
import { GradingService, GradingResult } from '../../../../core/services/grading.service';

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
    MatChipsModule,
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

    /* Image Preview Panel */
    .image-preview-panel {
      position: fixed;
      right: 20px;
      bottom: 20px;
      width: 375px;
      height: 525px;
      background: white;
      border: 1px solid #ccc;
      box-shadow: 0 4px 20px rgba(0,0,0,0.3);
      border-radius: 8px;
      z-index: 1000;
      padding: 10px;
      display: flex;
      justify-content: center;
      align-items: center;
      pointer-events: none;
      animation: slideIn 0.2s ease-out;
    }

    .image-preview-panel img {
      max-width: 100%;
      max-height: 100%;
      object-fit: contain;
      border-radius: 4px;
    }

    @keyframes slideIn {
      from { opacity: 0; transform: translateY(20px); }
      to { opacity: 1; transform: translateY(0); }
    }

    /* Grading condition colors */
    .condition-nm { background-color: #4caf50 !important; }
    .condition-sp { background-color: #8bc34a !important; }
    .condition-mp { background-color: #ff9800 !important; }
    .condition-pl { background-color: #ff5722 !important; }
    .condition-po { background-color: #f44336 !important; }
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
  private formSubscription?: Subscription;

  conditions = ['Near Mint', 'Slightly Played', 'Moderately Played', 'Played', 'Poor'];
  languages = ['English', 'Italian', 'Japanese', 'French', 'German', 'Spanish', 'Chinese'];

  // Grading state
  showGrading = signal(false);
  gradingImages = signal<{ dataUrl: string; label: string }[]>([]);
  gradingResult = signal<GradingResult | null>(null);
  isGrading = signal(false);
  currentGradingLabel = signal('Fronte');

  constructor(
    private fb: FormBuilder,
    private productsService: ProductsService,
    private pendingListingsService: PendingListingsService,
    private snackBar: MatSnackBar,
    private cardTraderService: CardTraderApiService,
    private gradingService: GradingService
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
      tag: [defaults.tag || ''],
      purchasePrice: [0, [Validators.required, Validators.min(0)]]
    });
    this.saveDefaults.set(defaults.saveEnabled);
  }

  ngOnInit() {
    this.loadPendingListings();

    // Listen for form changes to refresh marketplace stats
    this.formSubscription = this.listingForm.valueChanges.pipe(
      debounceTime(500),
      distinctUntilChanged((prev, curr) => {
        return prev.condition === curr.condition &&
          prev.language === curr.language &&
          prev.isFoil === curr.isFoil &&
          prev.isSigned === curr.isSigned;
      })
    ).subscribe(() => {
      if (this.selectedBlueprint()) {
        this.loadMarketplaceStats();
      }
    });
  }

  ngOnDestroy() {
    if (this.formSubscription) {
      this.formSubscription.unsubscribe();
    }
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
      tag: '',
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
      tag: this.listingForm.get('tag')?.value,
      saveEnabled: true
    };
    localStorage.setItem(this.STORAGE_KEY, JSON.stringify(defaults));
  }

  onBlueprintSelected(blueprint: Blueprint) {
    this.selectedBlueprint.set(blueprint);
    // Reset form but keep defaults if enabled
    this.resetFormState();
    // Load marketplace stats with current filters
    this.loadMarketplaceStats();
  }

  loadNextBlueprint(): void {
    const current = this.selectedBlueprint();
    if (!current || !current.expansionId || !current.fixedProperties) return;

    let collectorNumber = '';
    try {
      const props = JSON.parse(current.fixedProperties);
      collectorNumber = props.collector_number;
    } catch (e) {
      console.error('Error parsing fixed properties', e);
      return;
    }

    if (!collectorNumber) return;

    this.cardTraderService.getAdjacentBlueprint(current.expansionId, collectorNumber, 'next')
      .subscribe({
        next: (blueprint) => {
          if (blueprint) {
            this.onBlueprintSelected(blueprint);
          } else {
            this.snackBar.open('No next card found', 'Close', { duration: 2000 });
          }
        },
        error: (err) => {
          console.error('Error fetching next blueprint', err);
          this.snackBar.open('Error fetching next card', 'Close', { duration: 2000 });
        }
      });
  }

  loadPreviousBlueprint(): void {
    const current = this.selectedBlueprint();
    if (!current || !current.expansionId || !current.fixedProperties) return;

    let collectorNumber = '';
    try {
      const props = JSON.parse(current.fixedProperties);
      collectorNumber = props.collector_number;
    } catch (e) {
      console.error('Error parsing fixed properties', e);
      return;
    }

    if (!collectorNumber) return;

    this.cardTraderService.getAdjacentBlueprint(current.expansionId, collectorNumber, 'prev')
      .subscribe({
        next: (blueprint) => {
          if (blueprint) {
            this.onBlueprintSelected(blueprint);
          } else {
            this.snackBar.open('No previous card found', 'Close', { duration: 2000 });
          }
        },
        error: (err) => {
          console.error('Error fetching previous blueprint', err);
          this.snackBar.open('Error fetching previous card', 'Close', { duration: 2000 });
        }
      });
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
        this.pendingListings.set(response.data?.items || []);
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
    this.resetFormState();
  }

  private resetFormState() {
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
        tag: defaults.tag || ''
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

  // Image Preview Logic
  previewImage = signal<string | null>(null);

  showPreview(url: string | undefined) {
    if (url) {
      this.previewImage.set(url);
    }
  }

  hidePreview() {
    this.previewImage.set(null);
  }

  // Marketplace Stats
  marketplaceStats = signal<import('../../../../core/models').MarketplaceStats | null>(null);
  isLoadingStats = signal(false);

  loadMarketplaceStats() {
    const blueprint = this.selectedBlueprint();
    if (!blueprint) return;

    this.isLoadingStats.set(true);

    // Get current form filters
    const formValue = this.listingForm.value;
    const filters = {
      condition: formValue.condition,
      language: formValue.language,
      isFoil: formValue.isFoil,
      isSigned: formValue.isSigned
    };

    this.cardTraderService.getMarketplaceStats(blueprint.cardTraderId, filters).subscribe({
      next: (stats) => {
        this.marketplaceStats.set(stats);
        this.isLoadingStats.set(false);
      },
      error: (err) => {
        console.error('Error loading marketplace stats', err);
        this.isLoadingStats.set(false);
      }
    });
  }

  applyPrice(price: number) {
    this.listingForm.patchValue({ sellingPrice: price });
    this.listingForm.markAsDirty();
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
      purchasePrice: formValue.purchasePrice,
      // Include grading data if available
      gradingScore: this.gradingResult()?.overallGrade,
      gradingConditionCode: this.gradingResult()?.conditionCode,
      gradingCentering: this.gradingResult()?.centering,
      gradingCorners: this.gradingResult()?.corners,
      gradingEdges: this.gradingResult()?.edges,
      gradingSurface: this.gradingResult()?.surface,
      gradingConfidence: this.gradingResult()?.confidence,
      gradingImagesCount: this.gradingResult()?.imagesAnalyzed
    };

    if (this.editingId()) {
      // Update existing
      this.pendingListingsService.updatePendingListing(this.editingId()!, dto).subscribe({
        next: (updated) => {
          this.snackBar.open('Listing updated', 'Close', { duration: 3000 });
          this.isSubmitting.set(false);
          this.editingId.set(null);
          this.loadPendingListings();
          this.resetFormState();
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
          this.resetFormState();
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

  // Grading methods
  toggleGrading() {
    this.showGrading.update(v => !v);
    if (!this.showGrading()) {
      this.resetGrading();
    }
  }

  resetGrading() {
    this.gradingImages.set([]);
    this.gradingResult.set(null);
    this.currentGradingLabel.set('Fronte');
  }

  onGradingFilesSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      for (let i = 0; i < input.files.length; i++) {
        const file = input.files[i];
        const reader = new FileReader();
        reader.onload = (e) => {
          const images = this.gradingImages();
          images.push({
            dataUrl: e.target?.result as string,
            label: this.currentGradingLabel()
          });
          this.gradingImages.set([...images]);
          this.updateGradingLabel();
        };
        reader.readAsDataURL(file);
      }
    }
  }

  removeGradingImage(index: number) {
    const images = this.gradingImages();
    images.splice(index, 1);
    this.gradingImages.set([...images]);
    this.updateGradingLabel();
  }

  updateGradingLabel() {
    const count = this.gradingImages().length;
    if (count === 0) this.currentGradingLabel.set('Fronte');
    else if (count === 1) this.currentGradingLabel.set('Retro');
    else this.currentGradingLabel.set(`Foto ${count + 1}`);
  }

  async analyzeGrading() {
    const images = this.gradingImages();
    if (images.length === 0) return;

    this.isGrading.set(true);

    try {
      const files: File[] = [];
      for (const img of images) {
        const res = await fetch(img.dataUrl);
        const blob = await res.blob();
        files.push(new File([blob], `card-${img.label.toLowerCase()}.jpg`, { type: 'image/jpeg' }));
      }

      this.gradingService.analyzeCardMultiple(files).subscribe({
        next: (result) => {
          this.gradingResult.set(result);
          this.isGrading.set(false);
          // Auto-apply condition
          this.applyGradingCondition(result.conditionName);
        },
        error: (err) => {
          console.error('Grading error:', err);
          this.snackBar.open('Errore durante l\'analisi', 'Chiudi', { duration: 3000 });
          this.isGrading.set(false);
        }
      });
    } catch (err) {
      console.error('Error:', err);
      this.isGrading.set(false);
    }
  }

  applyGradingCondition(conditionName: string) {
    this.listingForm.patchValue({ condition: conditionName });
    this.snackBar.open(`Condizione impostata: ${conditionName}`, 'OK', { duration: 2000 });
  }

  getConditionClass(): string {
    const result = this.gradingResult();
    if (!result) return '';
    switch (result.conditionCode) {
      case 'NM': return 'condition-nm';
      case 'SP': return 'condition-sp';
      case 'MP': return 'condition-mp';
      case 'PL': return 'condition-pl';
      case 'PO': return 'condition-po';
      default: return '';
    }
  }
}
