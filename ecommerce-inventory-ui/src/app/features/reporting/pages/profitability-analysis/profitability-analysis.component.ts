import { Component, OnInit } from '@angular/core';
import { ReportingService } from '../../../../core/services/reporting.service';
import { ProfitabilityOverview, TopPerformer } from '../../../../core/models/reporting.models';
import { firstValueFrom } from 'rxjs';
import { ColDef, ValueFormatterParams } from 'ag-grid-community';

@Component({
    selector: 'app-profitability-analysis',
    templateUrl: './profitability-analysis.component.html',
    styleUrls: ['./profitability-analysis.component.css'],
    standalone: false
})
export class ProfitabilityAnalysisComponent implements OnInit {
    overview?: ProfitabilityOverview;
    topPerformers: TopPerformer[] = [];

    // AG-Grid Column Definitions
    topPerformersColumnDefs: ColDef[] = [
        { field: 'cardName', headerName: 'Card' },
        { field: 'expansionName', headerName: 'Expansion' },
        {
            field: 'profitMarginPercentage',
            headerName: 'Profit Margin',
            valueFormatter: (params: ValueFormatterParams) => params.value + '%'
        },
        {
            field: 'totalProfit',
            headerName: 'Total Profit',
            valueFormatter: (params: ValueFormatterParams) => '€' + params.value
        },
        { field: 'quantitySold', headerName: 'Qty Sold' }
    ];

    constructor(private reportingService: ReportingService) { }

    async ngOnInit(): Promise<void> {
        try {
            console.log('Fetching profitability analysis data...');
            const [overview, performers] = await Promise.all([
                firstValueFrom(this.reportingService.getProfitabilityOverview()),
                firstValueFrom(this.reportingService.getTopPerformers())
            ]);

            console.log('Profitability analysis data fetched successfully', { overview, performers });

            this.overview = overview;
            this.topPerformers = performers;
        } catch (error) {
            console.error('Error fetching profitability analysis data:', error);
        }
    }
}
