import { Component, OnInit } from '@angular/core';
import { ReportingService } from '../../../../core/services/reporting.service';
import { SalesMetrics, TopProduct, SalesChartData, DateRange } from '../../../../core/models/reporting.models';
import { firstValueFrom } from 'rxjs';
import { ColDef, GridApi, GridOptions, GridReadyEvent, ValueFormatterParams } from 'ag-grid-community';
import { GridStateService } from '../../../../core/services/grid-state.service';

@Component({
  selector: 'app-sales-dashboard',
  templateUrl: './sales-dashboard.html',
  styleUrls: ['./sales-dashboard.css'],
  standalone: false
})
export class SalesDashboard implements OnInit {
  salesMetrics?: SalesMetrics;
  topProducts: TopProduct[] = [];
  salesChartData?: SalesChartData;
  isLoading = false;

  // Date filters — default ultimo mese
  fromDate: string = this.toInputDate(new Date(Date.now() - 30 * 24 * 60 * 60 * 1000));
  toDate: string = this.toInputDate(new Date());

  // Chart.js
  chartLabels: string[] = [];
  chartValues: number[] = [];
  chartOptions: any = { responsive: true };

  private gridApi!: GridApi;
  private readonly GRID_ID = 'sales-top-products-grid';

  private readonly currencyFormatter = (p: ValueFormatterParams) =>
    p.value != null ? `€${(p.value as number).toFixed(2)}` : '';

  topProductsColumnDefs: ColDef[] = [
    { field: 'cardName', headerName: 'Carta', sortable: true, filter: true, flex: 2 },
    { field: 'expansionName', headerName: 'Espansione', sortable: true, filter: true, flex: 1 },
    {
      field: 'quantitySold',
      headerName: 'Qtà Venduta',
      sortable: true,
      filter: 'agNumberColumnFilter',
      width: 130
    },
    {
      field: 'totalRevenue',
      headerName: 'Fatturato',
      sortable: true,
      filter: 'agNumberColumnFilter',
      width: 130,
      sort: 'desc',
      valueFormatter: this.currencyFormatter
    },
    {
      field: 'averagePrice',
      headerName: 'Prezzo Medio',
      sortable: true,
      filter: 'agNumberColumnFilter',
      width: 130,
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
    await this.loadData();
  }

  async loadData(): Promise<void> {
    this.isLoading = true;
    const dateRange = this.buildDateRange();
    try {
      const [metrics, top, chart] = await Promise.all([
        firstValueFrom(this.reportingService.getSalesMetrics(dateRange)),
        firstValueFrom(this.reportingService.getTopProducts(dateRange, 20)),
        firstValueFrom(this.reportingService.getSalesChart(dateRange, this.chartGroupBy()))
      ]);

      this.salesMetrics = metrics;
      this.topProducts = top;
      this.salesChartData = chart;

      if (chart) {
        this.chartLabels = chart.labels;
        this.chartValues = chart.values;
      }
    } finally {
      this.isLoading = false;
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

  private buildDateRange(): DateRange {
    return {
      from: new Date(this.fromDate),
      to: new Date(this.toDate)
    };
  }

  private chartGroupBy(): 'day' | 'week' | 'month' {
    const from = new Date(this.fromDate);
    const to = new Date(this.toDate);
    const diffDays = (to.getTime() - from.getTime()) / (1000 * 60 * 60 * 24);
    if (diffDays <= 31) return 'day';
    if (diffDays <= 90) return 'week';
    return 'month';
  }

  private toInputDate(d: Date): string {
    return d.toISOString().substring(0, 10);
  }
}
