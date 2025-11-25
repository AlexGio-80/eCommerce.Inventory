import { Component, OnInit } from '@angular/core';
import { ReportingService } from '../../../../core/services/reporting.service';
import { SalesMetrics, TopProduct, SalesByGame, SalesChartData } from '../../../../core/models/reporting.models';
import { firstValueFrom } from 'rxjs';
import { ColDef, ValueFormatterParams } from 'ag-grid-community';

@Component({
  selector: 'app-sales-dashboard',
  templateUrl: './sales-dashboard.html',
  styleUrls: ['./sales-dashboard.css'],
  standalone: false
})
export class SalesDashboard implements OnInit {
  salesMetrics?: SalesMetrics;
  topProducts: TopProduct[] = [];
  salesByGame: SalesByGame[] = [];
  salesChartData?: SalesChartData;

  // Chart.js data
  chartLabels: string[] = [];
  chartValues: number[] = [];
  chartOptions: any = { responsive: true };

  // AG-Grid Column Definitions
  topProductsColumnDefs: ColDef[] = [
    { field: 'cardName', headerName: 'Card' },
    { field: 'expansionName', headerName: 'Expansion' },
    { field: 'gameName', headerName: 'Game' },
    { field: 'quantitySold', headerName: 'Qty Sold' },
    {
      field: 'totalRevenue',
      headerName: 'Revenue',
      valueFormatter: (params: ValueFormatterParams) => '$' + params.value
    }
  ];

  salesByGameColumnDefs: ColDef[] = [
    { field: 'gameName', headerName: 'Game' },
    {
      field: 'totalRevenue',
      headerName: 'Revenue',
      valueFormatter: (params: ValueFormatterParams) => '$' + params.value
    },
    { field: 'orderCount', headerName: 'Orders' },
    {
      field: 'percentage',
      headerName: '% of Total',
      valueFormatter: (params: ValueFormatterParams) => params.value + '%'
    }
  ];

  constructor(private reportingService: ReportingService) { }

  async ngOnInit(): Promise<void> {
    try {
      console.log('Fetching sales dashboard data...');
      const [metrics, top, byGame, chart] = await Promise.all([
        firstValueFrom(this.reportingService.getSalesMetrics()),
        firstValueFrom(this.reportingService.getTopProducts()),
        firstValueFrom(this.reportingService.getSalesByGame()),
        firstValueFrom(this.reportingService.getSalesChart())
      ]);

      console.log('Sales dashboard data fetched successfully', { metrics, top, byGame, chart });

      this.salesMetrics = metrics;
      this.topProducts = top;
      this.salesByGame = byGame;
      this.salesChartData = chart;
      if (chart) {
        this.chartLabels = chart.labels;
        this.chartValues = chart.values;
      }
    } catch (error) {
      console.error('Error fetching sales dashboard data:', error);
    }
  }
}

