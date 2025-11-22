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
      {
        path: 'products/create',
        loadComponent: () => import('./features/products/pages/create-listing/create-listing.component').then(m => m.CreateListingComponent),
        data: { title: 'Create Listing' }
      },
      // TODO: Add routes for Phase 3.3-3.5
      // - /layout/products (Product Listing Component)
      // - /layout/orders (Orders Component)
      // - /layout/reporting (Reporting Component)
      {
        path: 'orders',
        loadComponent: () => import('./features/orders/orders-list/orders-list.component').then(m => m.OrdersListComponent),
        data: { title: 'Orders' }
      },
      {
        path: 'orders/unprepared',
        loadComponent: () => import('./features/orders/unprepared-items/unprepared-items.component').then(m => m.UnpreparedItemsComponent),
        data: { title: 'Unprepared Items' }
      },
    ]
  },
  {
    path: '**',
    redirectTo: '/layout/dashboard'
  }
];
