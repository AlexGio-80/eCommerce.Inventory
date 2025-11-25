import { Component, OnInit } from '@angular/core';
import { ReportingService } from '../../../../core/services/reporting.service';
import { InventoryValue, InventoryDistribution, SlowMover } from '../../../../core/models/reporting.models';
import { firstValueFrom } from 'rxjs';
import { ColDef, ValueFormatterParams } from 'ag-grid-community';

@Component({
    selector: 'app-inventory-analytics',
    templateUrl: './inventory-analytics.component.html',
    styleUrls: ['./inventory-analytics.component.css'],
    standalone: false
})
export class InventoryAnalyticsComponent implements OnInit {
    inventoryValue?: InventoryValue;
    inventoryDistribution: InventoryDistribution[] = [];
    slowMovers: SlowMover[] = [];

    // Chart.js data
    distributionLabels: string[] = [];
    distributionValues: number[] = [];
    chartOptions: any = { responsive: true };

    // AG-Grid Column Definitions
    slowMoversColumnDefs: ColDef[] = [
        { field: 'cardName', headerName: 'Card' },
        { field: 'expansionName', headerName: 'Expansion' },
        { field: 'daysInInventory', headerName: 'Days in Inventory' },
        { field: 'quantity', headerName: 'Quantity' },
        {
            field: 'listingPrice',
            headerName: 'Price',
            valueFormatter: (params: ValueFormatterParams) => '€' + params.value
        }
    ];

    constructor(private reportingService: ReportingService) { }

    async ngOnInit(): Promise<void> {
        try {
            console.log('Fetching inventory analytics data...');
            const [value, distribution, slow] = await Promise.all([
                firstValueFrom(this.reportingService.getInventoryValue()),
                firstValueFrom(this.reportingService.getInventoryDistribution()),
                firstValueFrom(this.reportingService.getSlowMovers())
            ]);

            console.log('Inventory analytics data fetched successfully', { value, distribution, slow });

            this.inventoryValue = value;
            this.inventoryDistribution = distribution;
            this.slowMovers = slow;

            if (distribution) {
                this.distributionLabels = distribution.map(d => d.gameName);
                this.distributionValues = distribution.map(d => d.totalValue);
            }
        } catch (error) {
            console.error('Error fetching inventory analytics data:', error);
        }
    }
}
