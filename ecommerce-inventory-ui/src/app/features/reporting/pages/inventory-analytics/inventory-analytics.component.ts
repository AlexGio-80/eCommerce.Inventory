import { Component, OnInit } from '@angular/core';
import { ReportingService } from '../../../../core/services/reporting.service';
import { InventoryValue, SlowMover } from '../../../../core/models/reporting.models';
import { firstValueFrom } from 'rxjs';
import { ColDef, GridApi, GridOptions, GridReadyEvent, ValueFormatterParams } from 'ag-grid-community';
import { GridStateService } from '../../../../core/services/grid-state.service';

@Component({
    selector: 'app-inventory-analytics',
    templateUrl: './inventory-analytics.component.html',
    styleUrls: ['./inventory-analytics.component.css'],
    standalone: false
})
export class InventoryAnalyticsComponent implements OnInit {
    inventoryValue?: InventoryValue;
    slowMovers: SlowMover[] = [];
    isLoadingSlowMovers = false;
    slowMoverDays = 90;

    private gridApi!: GridApi;
    private readonly GRID_ID = 'inventory-slow-movers-grid';

    private readonly currencyFormatter = (p: ValueFormatterParams) =>
        p.value != null ? `€${(p.value as number).toFixed(2)}` : '';

    slowMoversColumnDefs: ColDef[] = [
        { field: 'cardName', headerName: 'Carta', sortable: true, filter: true, flex: 2 },
        { field: 'expansionName', headerName: 'Espansione', sortable: true, filter: true, flex: 1 },
        {
            field: 'daysInInventory',
            headerName: 'Giorni in Inv.',
            sortable: true,
            filter: 'agNumberColumnFilter',
            width: 140,
            sort: 'desc'
        },
        {
            field: 'quantity',
            headerName: 'Qtà',
            sortable: true,
            filter: 'agNumberColumnFilter',
            width: 90
        },
        {
            field: 'listingPrice',
            headerName: 'Prezzo',
            sortable: true,
            filter: 'agNumberColumnFilter',
            width: 110,
            valueFormatter: this.currencyFormatter
        }
    ];

    gridOptions: GridOptions = {
        sideBar: {
            toolPanels: [
                {
                    id: 'columns',
                    labelDefault: 'Colonne',
                    labelKey: 'columns',
                    iconKey: 'columns',
                    toolPanel: 'agColumnsToolPanel',
                    toolPanelParams: {
                        suppressRowGroups: true,
                        suppressValues: true,
                        suppressPivots: true,
                        suppressPivotMode: true
                    }
                }
            ]
        }
    };

    constructor(
        private reportingService: ReportingService,
        private gridStateService: GridStateService
    ) { }

    async ngOnInit(): Promise<void> {
        this.inventoryValue = await firstValueFrom(this.reportingService.getInventoryValue());
        await this.loadSlowMovers();
    }

    async loadSlowMovers(): Promise<void> {
        this.isLoadingSlowMovers = true;
        try {
            this.slowMovers = await firstValueFrom(this.reportingService.getSlowMovers(this.slowMoverDays));
        } finally {
            this.isLoadingSlowMovers = false;
        }
    }

    onGridReady(params: GridReadyEvent): void {
        this.gridApi = params.api;
        const saved = this.gridStateService.loadGridState(this.GRID_ID);
        if (saved?.columnState) {
            this.gridApi.applyColumnState({ state: saved.columnState, applyOrder: true });
        }
    }

    saveGridState(): void {
        if (!this.gridApi) return;
        const columnState = this.gridApi.getColumnState();
        this.gridStateService.saveGridState(this.GRID_ID, {
            columnState,
            sortModel: columnState.filter(c => c.sort != null)
        });
    }
}
