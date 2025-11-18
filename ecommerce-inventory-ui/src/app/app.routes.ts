import { Routes } from '@angular/router';
import { DashboardComponent } from './features/inventory/pages/dashboard/dashboard.component';
import { InventoryListComponent } from './features/inventory/pages/inventory-list/inventory-list.component';

export const routes: Routes = [
  {
    path: '',
    redirectTo: '/dashboard',
    pathMatch: 'full'
  },
  {
    path: 'dashboard',
    component: DashboardComponent,
    data: { title: 'Dashboard' }
  },
  {
    path: 'inventory',
    component: InventoryListComponent,
    data: { title: 'Inventario' }
  },
  // TODO: Add routes for Phase 3.2-3.5
  // - /sync (Sync Component)
  // - /products (Product Listing Component)
  // - /orders (Orders Component)
  // - /reporting (Reporting Component)
  {
    path: '**',
    redirectTo: '/dashboard'
  }
];
