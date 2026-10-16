import { Routes } from '@angular/router';
import { MobileShellComponent } from './mobile-shell.component';
import { MobileScanComponent } from './mobile-scan.component';
import { MobileSearchComponent } from './mobile-search.component';
import { MobileCardComponent } from './mobile-card.component';

/** Sezione per il telefono (feature 006): Scansiona, Cerca, scheda di valutazione. */
export const MOBILE_ROUTES: Routes = [
  {
    path: '',
    component: MobileShellComponent,
    children: [
      { path: '', redirectTo: 'scan', pathMatch: 'full' },
      { path: 'scan', component: MobileScanComponent },
      { path: 'search', component: MobileSearchComponent },
      { path: 'card/:id', component: MobileCardComponent }
    ]
  }
];
