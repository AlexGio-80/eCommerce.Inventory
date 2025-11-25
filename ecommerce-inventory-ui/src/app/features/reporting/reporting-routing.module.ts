import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { SalesDashboard } from './pages/sales-dashboard/sales-dashboard';
import { InventoryAnalyticsComponent } from './pages/inventory-analytics/inventory-analytics.component';
import { ProfitabilityAnalysisComponent } from './pages/profitability-analysis/profitability-analysis.component';

const routes: Routes = [
    { path: '', redirectTo: 'sales', pathMatch: 'full' },
    { path: 'sales', component: SalesDashboard },
    { path: 'inventory', component: InventoryAnalyticsComponent },
    { path: 'profitability', component: ProfitabilityAnalysisComponent }
];

@NgModule({
    imports: [RouterModule.forChild(routes)],
    exports: [RouterModule]
})
export class ReportingRoutingModule { }
