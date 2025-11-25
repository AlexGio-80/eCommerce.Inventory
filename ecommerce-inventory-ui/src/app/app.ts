import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ReportingModule } from './features/reporting/reporting.module';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, ReportingModule],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  protected readonly title = signal('ecommerce-inventory-ui');
}

