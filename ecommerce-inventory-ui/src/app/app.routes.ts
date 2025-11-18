import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    redirectTo: '/dashboard',
    pathMatch: 'full'
  },
  // Routes will be populated in Phase 3.1 when components are created
  // Dashboard, Inventory, Sync, Orders, Products, Reporting
  {
    path: '**',
    redirectTo: '/dashboard'
  }
];
