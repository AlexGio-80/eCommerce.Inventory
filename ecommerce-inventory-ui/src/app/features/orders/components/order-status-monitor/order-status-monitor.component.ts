import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { SignalRService } from '../../../../core/services/signalr.service';
import { Subscription } from 'rxjs';

interface OrderEvent {
  type: 'Created' | 'Updated';
  id: number;
  timestamp: Date;
  data: any;
}

@Component({
  selector: 'app-order-status-monitor',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatListModule, MatIconModule],
  template: `
    <mat-card class="monitor-card">
      <mat-card-header>
        <mat-card-title>
          <mat-icon color="accent">notifications_active</mat-icon>
          Live Order Updates
        </mat-card-title>
      </mat-card-header>
      <mat-card-content>
        <mat-list>
          <mat-list-item *ngFor="let event of events">
            <mat-icon matListItemIcon [color]="event.type === 'Created' ? 'primary' : 'accent'">
              {{ event.type === 'Created' ? 'add_shopping_cart' : 'update' }}
            </mat-icon>
            <div matListItemTitle>Order #{{ event.id }}</div>
            <div matListItemLine>{{ event.type }} - {{ event.timestamp | date:'mediumTime' }}</div>
          </mat-list-item>
        </mat-list>
        <div *ngIf="events.length === 0" class="no-events">
          No recent updates
        </div>
        </mat-card-content>
    </mat-card>
  `,
  styles: [`
    .monitor-card {
      margin-bottom: 16px;
    }
    .no-events {
      padding: 16px;
      text-align: center;
      color: #888;
      font-style: italic;
    }
  `]
})
export class OrderStatusMonitorComponent implements OnInit, OnDestroy {
  events: OrderEvent[] = [];
  private subscriptions: Subscription[] = [];

  constructor(private signalRService: SignalRService) { }

  ngOnInit() {
    this.subscriptions.push(
      this.signalRService.orderCreated$.subscribe(data => {
        if (data) {
          this.addEvent('Created', data);
        }
      })
    );

    this.subscriptions.push(
      this.signalRService.orderUpdated$.subscribe(data => {
        if (data) {
          this.addEvent('Updated', data);
        }
      })
    );
  }

  ngOnDestroy() {
    this.subscriptions.forEach(sub => sub.unsubscribe());
  }

  private addEvent(type: 'Created' | 'Updated', data: any) {
    this.events.unshift({
      type,
      id: data.id,
      timestamp: new Date(),
      data
    });

    // Keep only last 5 events
    if (this.events.length > 5) {
      this.events.pop();
    }
  }
}
