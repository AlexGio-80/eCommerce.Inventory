import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { InventoryItem } from '../../../core/models/inventory-item';

@Component({
  selector: 'app-edit-inventory-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatCheckboxModule,
    MatButtonModule
  ],
  templateUrl: './edit-inventory-dialog.component.html',
  styles: [`
    .dialog-content {
      min-width: 500px;
      max-width: 600px;
    }

    .form-row {
      display: flex;
      gap: 16px;
      margin-bottom: 16px;
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

    .dialog-actions {
      display: flex;
      justify-content: flex-end;
      gap: 8px;
      margin-top: 24px;
    }

    .card-info {
      margin-bottom: 16px;
      padding: 12px;
      background-color: #f5f5f5;
      border-radius: 4px;
    }

    .card-info h3 {
      margin: 0 0 4px 0;
      font-size: 16px;
    }

    .card-info p {
      margin: 0;
      color: #666;
      font-size: 14px;
    }
  `]
})
export class EditInventoryDialogComponent {
  editForm: FormGroup;

  conditions = ['Near Mint', 'Slightly Played', 'Moderately Played', 'Played', 'Poor'];
  languages = ['English', 'Italian', 'Japanese', 'French', 'German', 'Spanish', 'Chinese'];

  constructor(
    private fb: FormBuilder,
    public dialogRef: MatDialogRef<EditInventoryDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: InventoryItem
  ) {
    this.editForm = this.fb.group({
      quantity: [data.quantity, [Validators.required, Validators.min(1)]],
      listingPrice: [data.listingPrice, [Validators.required, Validators.min(0)]],
      condition: [data.condition, Validators.required],
      language: [data.language], // Optional
      tag: [data.tag || ''], // Optional
      isFoil: [data.isFoil],
      isSigned: [data.isSigned],
      isAltered: [data.isAltered]
    });
  }

  onCancel(): void {
    this.dialogRef.close();
  }

  onSave(): void {
    if (this.editForm.valid) {
      this.dialogRef.close(this.editForm.value);
    }
  }
}
