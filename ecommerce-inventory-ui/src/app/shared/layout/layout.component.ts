import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet, RouterLink, Router } from '@angular/router';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';

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
  ],
  templateUrl: './layout.component.html',
  styleUrls: ['./layout.component.scss'],
})
export class LayoutComponent {
  isSidenavOpen = signal(true);

  navItems: NavItem[] = [
    { label: 'Dashboard', route: '/layout/dashboard', icon: 'dashboard' },
    { label: 'Inventario', route: '/layout/inventory', icon: 'inventory_2' },
    { label: 'Sincronizzazione', route: '/layout/sync', icon: 'sync' },
    { label: 'Games', route: '/layout/games', icon: 'casino' },
    { label: 'Expansions', route: '/layout/expansions', icon: 'extension' },
    { label: 'Prodotti', route: '/layout/products', icon: 'shopping_bag' },
    { label: 'Ordini', route: '/layout/orders', icon: 'shopping_cart' },
    { label: 'Report', route: '/layout/reporting', icon: 'bar_chart' },
  ];

  constructor(private router: Router) { }

  toggleSidenav(): void {
    this.isSidenavOpen.update((val) => !val);
  }

  navigateTo(route: string): void {
    this.router.navigate([route]);
    // Chiudi sidenav su mobile quando clicchi un link
    if (window.innerWidth < 768) {
      this.isSidenavOpen.set(false);
    }
  }

  isActive(route: string): boolean {
    return this.router.url === route;
  }
}
