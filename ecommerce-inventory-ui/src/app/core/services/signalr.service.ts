import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject, Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { MatSnackBar } from '@angular/material/snack-bar';

@Injectable({
    providedIn: 'root'
})
export class SignalRService {
    private hubConnection!: signalR.HubConnection;
    private connectionStatus = new BehaviorSubject<string>('Disconnected');

    // Events
    public orderCreated$ = new BehaviorSubject<any>(null);
    public orderUpdated$ = new BehaviorSubject<any>(null);

    constructor(private snackBar: MatSnackBar) {
        this.buildConnection();
        this.startConnection();
        this.registerOnServerEvents();
    }

    private buildConnection() {
        this.hubConnection = new signalR.HubConnectionBuilder()
            .withUrl(`${environment.apiUrl}/notificationHub`)
            .withAutomaticReconnect()
            .build();
    }

    private startConnection() {
        this.hubConnection
            .start()
            .then(() => {
                console.log('SignalR Connection Started');
                this.connectionStatus.next('Connected');
            })
            .catch((err: any) => {
                console.error('Error while starting SignalR connection: ' + err);
                this.connectionStatus.next('Error');
                setTimeout(() => this.startConnection(), 5000);
            });
    }

    private registerOnServerEvents() {
        this.hubConnection.on('OrderCreated', (data: any) => {
            console.log('Order Created Event:', data);
            this.orderCreated$.next(data);
            this.showNotification(`New Order Received! ID: ${data.id}`);
        });

        this.hubConnection.on('OrderUpdated', (data: any) => {
            console.log('Order Updated Event:', data);
            this.orderUpdated$.next(data);
            this.showNotification(`Order Updated! ID: ${data.id}`);
        });
    }

    private showNotification(message: string) {
        this.snackBar.open(message, 'Close', {
            duration: 5000,
            horizontalPosition: 'end',
            verticalPosition: 'top'
        });
    }
}
