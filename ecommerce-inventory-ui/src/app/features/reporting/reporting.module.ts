import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { BaseChartDirective } from 'ng2-charts';
import { AgGridModule } from 'ag-grid-angular';
import { Chart, registerables } from 'chart.js';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

import { ReportingRoutingModule } from './reporting-routing.module';
import { SalesDashboard } from './pages/sales-dashboard/sales-dashboard';
import { InventoryAnalyticsComponent } from './pages/inventory-analytics/inventory-analytics.component';

@NgModule({
    declarations: [
        SalesDashboard,
        InventoryAnalyticsComponent
    ],
    imports: [
        CommonModule,
        FormsModule,
        BaseChartDirective,
        AgGridModule,
        ReportingRoutingModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
        MatIconModule
    ],
    exports: [
        SalesDashboard,
        InventoryAnalyticsComponent
    ]
})
export class ReportingModule {
    constructor() {
        Chart.register(...registerables);
    }
}
