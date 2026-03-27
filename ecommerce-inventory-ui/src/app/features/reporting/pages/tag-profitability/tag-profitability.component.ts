import { Component, OnInit } from '@angular/core';
import { ReportingService } from '../../../../core/services/reporting.service';
import { TagProfitability, TagExpansionProfitability } from '../../../../core/models/reporting.models';
import { firstValueFrom } from 'rxjs';
import { ColDef, GridApi, GridOptions, GridReadyEvent, RowClickedEvent, ValueFormatterParams } from 'ag-grid-community';
import { GridStateService } from '../../../../core/services/grid-state.service';

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

    private tagGridApi!: GridApi;
    private expansionGridApi!: GridApi;

    private readonly TAG_GRID_ID = 'tag-profitability-tags-grid';
    private readonly EXPANSION_GRID_ID = 'tag-profitability-expansions-grid';

    private readonly currencyFormatter = (p: ValueFormatterParams) =>
        p.value != null ? `€${(p.value as number).toFixed(2)}` : '';

    private readonly percentFormatter = (p: ValueFormatterParams) =>
        p.value != null ? `${(p.value as number).toFixed(1)}%` : '';

    private readonly profitCellStyle = (p: any) => ({
        color: p.value >= 0 ? '#2e7d32' : '#c62828',
        fontWeight: 'bold'
    });

    private readonly diffCellStyle = (p: any) => ({
        color: p.value >= 0 ? '#2e7d32' : '#c62828'
    });

    tagColumnDefs: ColDef[] = [
        { field: 'tag', headerName: 'Tag', sortable: true, filter: true, width: 180 },
        {
            field: 'totaleAcquistato',
            headerName: 'Acquistato €',
            sortable: true,
            width: 140,
            valueFormatter: this.currencyFormatter
        },
        {
            field: 'totaleVenduto',
            headerName: 'Venduto €',
            sortable: true,
            width: 140,
            valueFormatter: this.currencyFormatter
        },
        {
            field: 'differenza',
            headerName: 'Guadagno €',
            sortable: true,
            width: 140,
            cellStyle: this.profitCellStyle,
            valueFormatter: this.currencyFormatter
        },
        {
            field: 'percentualeDifferenza',
            headerName: '% Guadagno',
            sortable: true,
            width: 130,
            cellStyle: this.diffCellStyle,
            valueFormatter: this.percentFormatter
        },
        { field: 'quantitaVenduta', headerName: 'Qtà Venduta', sortable: true, width: 120 },
        { field: 'qtaRimanente', headerName: 'Qtà Rimanente', sortable: true, width: 130 },
        {
            field: 'valoreRimanente',
            headerName: 'Valore Rimanente €',
            sortable: true,
            width: 160,
            valueFormatter: this.currencyFormatter
        }
    ];

    expansionColumnDefs: ColDef[] = [
        { field: 'expansionName', headerName: 'Espansione', sortable: true, filter: true, flex: 1 },
        {
            field: 'totaleAcquistato',
            headerName: 'Acquistato €',
            sortable: true,
            width: 140,
            valueFormatter: this.currencyFormatter
        },
        {
            field: 'totaleVenduto',
            headerName: 'Venduto €',
            sortable: true,
            width: 140,
            valueFormatter: this.currencyFormatter
        },
        {
            field: 'differenza',
            headerName: 'Guadagno €',
            sortable: true,
            width: 140,
            cellStyle: this.profitCellStyle,
            valueFormatter: this.currencyFormatter
        },
        {
            field: 'percentualeDifferenza',
            headerName: '% Guadagno',
            sortable: true,
            width: 130,
            cellStyle: this.diffCellStyle,
            valueFormatter: this.percentFormatter
        },
        { field: 'quantitaVenduta', headerName: 'Qtà Venduta', sortable: true, width: 120 },
        { field: 'qtaRimanente', headerName: 'Qtà Rimanente', sortable: true, width: 130 },
        {
            field: 'valoreRimanente',
            headerName: 'Valore Rimanente €',
            sortable: true,
            width: 160,
            valueFormatter: this.currencyFormatter
        }
    ];

    tagGridOptions: GridOptions = {
        sideBar: {
            toolPanels: [
                {
                    id: 'columns',
                    labelDefault: 'Colonne',
                    labelKey: 'columns',
                    iconKey: 'columns',
                    toolPanel: 'agColumnsToolPanel',
                    toolPanelParams: { suppressRowGroups: true, suppressValues: true, suppressPivots: true, suppressPivotMode: true }
                }
            ]
        }
    };

    expansionGridOptions: GridOptions = {
        sideBar: {
            toolPanels: [
                {
                    id: 'columns',
                    labelDefault: 'Colonne',
                    labelKey: 'columns',
                    iconKey: 'columns',
                    toolPanel: 'agColumnsToolPanel',
                    toolPanelParams: { suppressRowGroups: true, suppressValues: true, suppressPivots: true, suppressPivotMode: true }
                }
            ]
        }
    };

    constructor(
        private reportingService: ReportingService,
        private gridStateService: GridStateService
    ) { }

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

    onTagGridReady(params: GridReadyEvent): void {
        this.tagGridApi = params.api;
        const saved = this.gridStateService.loadGridState(this.TAG_GRID_ID);
        if (saved?.columnState) {
            this.tagGridApi.applyColumnState({ state: saved.columnState, applyOrder: true });
        }
    }

    onExpansionGridReady(params: GridReadyEvent): void {
        this.expansionGridApi = params.api;
        const saved = this.gridStateService.loadGridState(this.EXPANSION_GRID_ID);
        if (saved?.columnState) {
            this.expansionGridApi.applyColumnState({ state: saved.columnState, applyOrder: true });
        }
    }

    saveTagGridState(): void {
        if (!this.tagGridApi) return;
        const columnState = this.tagGridApi.getColumnState();
        this.gridStateService.saveGridState(this.TAG_GRID_ID, {
            columnState,
            sortModel: columnState.filter(c => c.sort != null)
        });
    }

    saveExpansionGridState(): void {
        if (!this.expansionGridApi) return;
        const columnState = this.expansionGridApi.getColumnState();
        this.gridStateService.saveGridState(this.EXPANSION_GRID_ID, {
            columnState,
            sortModel: columnState.filter(c => c.sort != null)
        });
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
