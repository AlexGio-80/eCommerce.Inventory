import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BaseChartDirective } from 'ng2-charts';
import { AgGridModule } from 'ag-grid-angular';
import { Chart, registerables } from 'chart.js';

import { ReportingRoutingModule } from './reporting-routing.module';
import { SalesDashboard } from './pages/sales-dashboard/sales-dashboard';
import { InventoryAnalyticsComponent } from './pages/inventory-analytics/inventory-analytics.component';
import { ProfitabilityAnalysisComponent } from './pages/profitability-analysis/profitability-analysis.component';

import { ModuleRegistry, AllCommunityModule } from 'ag-grid-community';

// Register AG Grid modules
ModuleRegistry.registerModules([AllCommunityModule]);

@NgModule({
    declarations: [
        SalesDashboard,
        InventoryAnalyticsComponent,
        ProfitabilityAnalysisComponent
    ],
    imports: [
        CommonModule,
        BaseChartDirective,
        AgGridModule,
        ReportingRoutingModule
    ],
    exports: [
        SalesDashboard,
        InventoryAnalyticsComponent,
        ProfitabilityAnalysisComponent
    ]
})
export class ReportingModule {
    constructor() {
        Chart.register(...registerables);
    }
}
