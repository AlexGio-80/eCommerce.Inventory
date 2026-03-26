import { Component, OnInit } from '@angular/core';
import { ReportingService } from '../../../../core/services/reporting.service';
import { TagProfitability, TagExpansionProfitability } from '../../../../core/models/reporting.models';
import { firstValueFrom } from 'rxjs';
import { ColDef, GridApi, GridReadyEvent, RowClickedEvent, ValueFormatterParams } from 'ag-grid-community';

@Component({
    selector: 'app-tag-profitability',
    templateUrl: './tag-profitability.component.html',
    styleUrls: ['./tag-profitability.component.css'],
    standalone: false
})
export class TagProfitabilityComponent implements OnInit {
    tags: TagProfitability[] = [];
    expansions: TagExpansionProfitability[] = [];
    selectedTag: string | null = null;
    isLoading = false;
    isLoadingExpansions = false;

    private gridApi!: GridApi;

    tagColumnDefs: ColDef[] = [
        { field: 'tag', headerName: 'Tag', sortable: true, filter: true, width: 180 },
        {
            field: 'totaleAcquistato',
            headerName: 'Acquistato €',
            sortable: true,
            width: 140,
            valueFormatter: (p: ValueFormatterParams) => p.value != null ? `€${(p.value as number).toFixed(2)}` : ''
        },
        {
            field: 'totaleVenduto',
            headerName: 'Venduto €',
            sortable: true,
            width: 140,
            valueFormatter: (p: ValueFormatterParams) => p.value != null ? `€${(p.value as number).toFixed(2)}` : ''
        },
        {
            field: 'differenza',
            headerName: 'Guadagno €',
            sortable: true,
            width: 140,
            cellStyle: (p: any) => ({ color: p.value >= 0 ? '#2e7d32' : '#c62828', fontWeight: 'bold' }),
            valueFormatter: (p: ValueFormatterParams) => p.value != null ? `€${(p.value as number).toFixed(2)}` : ''
        },
        {
            field: 'percentualeDifferenza',
            headerName: '% Guadagno',
            sortable: true,
            width: 130,
            cellStyle: (p: any) => ({ color: p.value >= 0 ? '#2e7d32' : '#c62828' }),
            valueFormatter: (p: ValueFormatterParams) => p.value != null ? `${(p.value as number).toFixed(1)}%` : ''
        },
        { field: 'quantitaVenduta', headerName: 'Qtà Venduta', sortable: true, width: 120 }
    ];

    expansionColumnDefs: ColDef[] = [
        { field: 'expansionName', headerName: 'Espansione', sortable: true, filter: true, flex: 1 },
        {
            field: 'totaleAcquistato',
            headerName: 'Acquistato €',
            sortable: true,
            width: 140,
            valueFormatter: (p: ValueFormatterParams) => p.value != null ? `€${(p.value as number).toFixed(2)}` : ''
        },
        {
            field: 'totaleVenduto',
            headerName: 'Venduto €',
            sortable: true,
            width: 140,
            valueFormatter: (p: ValueFormatterParams) => p.value != null ? `€${(p.value as number).toFixed(2)}` : ''
        },
        {
            field: 'differenza',
            headerName: 'Guadagno €',
            sortable: true,
            width: 140,
            cellStyle: (p: any) => ({ color: p.value >= 0 ? '#2e7d32' : '#c62828', fontWeight: 'bold' }),
            valueFormatter: (p: ValueFormatterParams) => p.value != null ? `€${(p.value as number).toFixed(2)}` : ''
        },
        {
            field: 'percentualeDifferenza',
            headerName: '% Guadagno',
            sortable: true,
            width: 130,
            cellStyle: (p: any) => ({ color: p.value >= 0 ? '#2e7d32' : '#c62828' }),
            valueFormatter: (p: ValueFormatterParams) => p.value != null ? `${(p.value as number).toFixed(1)}%` : ''
        },
        { field: 'quantitaVenduta', headerName: 'Qtà Venduta', sortable: true, width: 120 }
    ];

    constructor(private reportingService: ReportingService) { }

    async ngOnInit(): Promise<void> {
        await this.loadTags();
    }

    async loadTags(): Promise<void> {
        this.isLoading = true;
        try {
            this.tags = await firstValueFrom(this.reportingService.getTagProfitability());
        } catch (error) {
            console.error('Error fetching tag profitability', error);
        } finally {
            this.isLoading = false;
        }
    }

    onGridReady(params: GridReadyEvent): void {
        this.gridApi = params.api;
    }

    async onTagRowClicked(event: RowClickedEvent): Promise<void> {
        const tag = (event.data as TagProfitability).tag;
        if (this.selectedTag === tag) {
            return;
        }
        this.selectedTag = tag;
        this.expansions = [];
        this.isLoadingExpansions = true;
        try {
            this.expansions = await firstValueFrom(this.reportingService.getTagExpansionProfitability(tag));
        } catch (error) {
            console.error('Error fetching expansion profitability for tag', tag, error);
        } finally {
            this.isLoadingExpansions = false;
        }
    }
}
