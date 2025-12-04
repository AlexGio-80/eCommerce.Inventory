import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { GradingRoutingModule } from './grading-routing.module';
import { GradingPageComponent } from './pages/grading-page/grading-page.component';

@NgModule({
  declarations: [
    GradingPageComponent
  ],
  imports: [
    CommonModule,
    GradingRoutingModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    MatSnackBarModule,
    MatProgressSpinnerModule
  ]
})
export class GradingModule { }
