import { Component, OnInit } from '@angular/core';
import { ReportingService } from '../../../../core/services/reporting.service';
import { InventoryValue, InventoryDistribution, SlowMover, TopExpansionValue } from '../../../../core/models/reporting.models';
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
    topExpansionsByValue: TopExpansionValue[] = [];

    // Chart.js data
    distributionLabels: string[] = [];
    distributionValues: number[] = [];
    chartOptions: any = { responsive: true };

    // AG-Grid Column Definitions
    slowMoversColumnDefs: ColDef[] = [
        { field: 'cardName', headerName: 'Carta' },
        { field: 'expansionName', headerName: 'Espansione' },
        { field: 'daysInInventory', headerName: 'Giorni in Inventario' },
        { field: 'quantity', headerName: 'Quantità' },
        {
            field: 'listingPrice',
            headerName: 'Prezzo',
            valueFormatter: (params: ValueFormatterParams) => '€' + params.value
        }
    ];

    topExpansionsColumnDefs: ColDef[] = [
        { field: 'expansionName', headerName: 'Espansione', flex: 2 },
        { field: 'gameName', headerName: 'Gioco', flex: 1 },
        {
            field: 'averageCardValue',
            headerName: 'Val. Medio Carta',
            flex: 1,
            valueFormatter: (params: ValueFormatterParams) => '€' + params.value.toFixed(2)
        },
        {
            field: 'totalMinPrice',
            headerName: 'Valore Totale (Min)',
            flex: 1,
            valueFormatter: (params: ValueFormatterParams) => '€' + params.value.toFixed(2)
        }
    ];

    constructor(private reportingService: ReportingService) { }

    async ngOnInit(): Promise<void> {
        try {
            console.log('Fetching inventory analytics data...');
            const [value, distribution, slow, topValues] = await Promise.all([
                firstValueFrom(this.reportingService.getInventoryValue()),
                firstValueFrom(this.reportingService.getInventoryDistribution()),
                firstValueFrom(this.reportingService.getSlowMovers()),
                firstValueFrom(this.reportingService.getTopExpansionsByValue())
            ]);

            console.log('Inventory analytics data fetched successfully', { value, distribution, slow, topValues });

            this.inventoryValue = value;
            this.inventoryDistribution = distribution;
            this.slowMovers = slow;
            this.topExpansionsByValue = topValues;

            if (distribution) {
                this.distributionLabels = distribution.map(d => d.gameName);
                this.distributionValues = distribution.map(d => d.totalValue);
            }
        } catch (error) {
            console.error('Error fetching inventory analytics data:', error);
        }
    }
}
