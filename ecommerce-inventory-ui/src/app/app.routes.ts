import { Routes } from '@angular/router';
import { LayoutComponent } from './shared/layout/layout.component';
import { DashboardComponent } from './features/inventory/pages/dashboard/dashboard.component';
import { InventoryListComponent } from './features/inventory/pages/inventory-list/inventory-list.component';
import { SyncPageComponent } from './features/sync/pages/sync-page.component';

export const routes: Routes = [
  {
    path: '',
    redirectTo: '/layout/dashboard',
    pathMatch: 'full'
  },
  {
    path: 'layout',
    component: LayoutComponent,
    children: [
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
      {
        path: 'sync',
        component: SyncPageComponent,
        data: { title: 'Sincronizzazione' }
      },
      {
        path: 'expansions',
        loadComponent: () => import('./features/expansions/pages/expansions-page.component').then(m => m.ExpansionsPageComponent),
        data: { title: 'Expansions' }
      },
      {
        path: 'games',
        loadComponent: () => import('./features/games/pages/games-page.component').then(m => m.GamesPageComponent),
        data: { title: 'Games' }
      },
      // TODO: Add routes for Phase 3.3-3.5
      // - /layout/products (Product Listing Component)
      // - /layout/orders (Orders Component)
      // - /layout/reporting (Reporting Component)
    ]
  },
  {
    path: '**',
    redirectTo: '/layout/dashboard'
  }
];
