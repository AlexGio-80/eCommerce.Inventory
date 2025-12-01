import { Component, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet, RouterLink, Router } from '@angular/router';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatMenuModule } from '@angular/material/menu';
import { TabBarComponent } from '../components/tab-bar/tab-bar.component';
import { TabManagerService } from '../../core/services';
import { AuthService } from '../../core/services/auth.service';

interface NavItem {
  label: string;
  route: string;
  icon: string;
}

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    MatSidenavModule,
    MatToolbarModule,
    MatListModule,
    MatIconModule,
    MatButtonModule,
    MatTooltipModule,
    MatMenuModule,
    TabBarComponent,
  ],
  templateUrl: './layout.component.html',
  styleUrls: ['./layout.component.scss'],
})
export class LayoutComponent implements OnInit {
  isSidenavOpen = signal(true);

  navItems: NavItem[] = [
    { label: 'Dashboard', route: '/layout/dashboard', icon: 'dashboard' },
    { label: 'Inventario', route: '/layout/inventory', icon: 'inventory_2' },
    { label: 'Sincronizzazione', route: '/layout/sync', icon: 'sync' },
    { label: 'Games', route: '/layout/games', icon: 'casino' },
    { label: 'Expansions', route: '/layout/expansions', icon: 'extension' },
    { label: 'Nuovo Prodotto', route: '/layout/products/create', icon: 'add_circle' },
    { label: 'Ordini', route: '/layout/orders', icon: 'shopping_cart' },
    { label: 'Da Preparare', route: '/layout/orders/unprepared', icon: 'checklist' },
    { label: 'Report Vendite', route: '/layout/reporting/sales', icon: 'monetization_on' },
    { label: 'Report Inventario', route: '/layout/reporting/inventory', icon: 'inventory' },
    { label: 'Report Redditività', route: '/layout/reporting/profitability', icon: 'trending_up' },
  ];

  constructor(
    private router: Router,
    private tabManager: TabManagerService,
    private authService: AuthService
  ) { }

  ngOnInit(): void {
    // Initialize tab based on current route
    const currentRoute = this.router.url;
    const navItem = this.navItems.find(item => item.route === currentRoute);

    if (navItem) {
      const tabId = this.tabManager.openTab(navItem.route, navItem.label, navItem.icon);
      this.tabManager.setActiveTab(tabId);
    }
  }

  logout(): void {
    this.authService.logout();
    this.router.navigate(['/login']);
  }

  toggleSidenav(): void {
    this.isSidenavOpen.update((val) => !val);
  }

  navigateTo(route: string): void {
    // Trova il nav item per ottenere label e icon
    const navItem = this.navItems.find(item => item.route === route);

    if (navItem) {
      // Apri o attiva il tab
      const tabId = this.tabManager.openTab(route, navItem.label, navItem.icon);
      this.tabManager.setActiveTab(tabId);
      this.router.navigate([route]);
    } else {
      // Fallback per route non trovate
      this.router.navigate([route]);
    }

    // Chiudi sidenav su mobile quando clicchi un link
    if (window.innerWidth < 768) {
      this.isSidenavOpen.set(false);
    }
  }

  isActive(route: string): boolean {
    return this.router.url === route;
  }
}
