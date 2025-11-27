import { Routes } from '@angular/router';
import { LayoutComponent } from './shared/layout/layout.component';
import { DashboardComponent } from './features/inventory/pages/dashboard/dashboard.component';
import { InventoryListComponent } from './features/inventory/pages/inventory-list/inventory-list.component';
import { SyncPageComponent } from './features/sync/pages/sync-page.component';
import { LoginComponent } from './features/auth/pages/login/login.component';
import { AuthGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  {
    path: '',
    redirectTo: '/layout/dashboard',
    pathMatch: 'full'
  },
  {
    path: 'login',
    component: LoginComponent
  },
  {
    path: 'layout',
    component: LayoutComponent,
    canActivate: [AuthGuard],
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
      {
        path: 'reporting',
        loadChildren: () => import('./features/reporting/reporting.module').then(m => m.ReportingModule),
        data: { title: 'Reporting' }
      }
    ]
  },
  {
    path: '**',
    redirectTo: '/layout/dashboard'
  }
];
