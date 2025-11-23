import { Injectable } from '@angular/core';
import { GridApi, ColDef } from 'ag-grid-community';
import * as XLSX from 'xlsx';

/**
 * Service for exporting grid data to various formats (CSV, Excel)
 * Centralizes export logic to ensure consistency across all grids
 */
@Injectable({
  providedIn: 'root'
})
export class ExportService {

  constructor() { }

  /**
   * Export grid data to CSV format using AG-Grid's built-in functionality
   * @param gridApi - AG-Grid API instance
   * @param filename - Name of the file (without extension)
   * @param selectedOnly - If true, export only selected rows
   */
  exportToCsv(gridApi: GridApi, filename: string, selectedOnly: boolean = false): void {
    try {
      const params = {
        fileName: `${filename}.csv`,
        onlySelected: selectedOnly,
        processCellCallback: (params: any) => {
          // Format dates and numbers for CSV
          if (params.value instanceof Date) {
            return params.value.toISOString().split('T')[0];
          }
          if (typeof params.value === 'number') {
            return params.value.toString();
          }
          return params.value;
        }
      };

      gridApi.exportDataAsCsv(params);
    } catch (error) {
      console.error('Error exporting to CSV:', error);
      throw new Error('Failed to export data to CSV');
    }
  }

  /**
   * Export grid data to Excel format using xlsx library
   * @param data - Array of data objects to export
   * @param columns - Column definitions (used for headers and field mapping)
   * @param filename - Name of the file (without extension)
   * @param sheetName - Name of the Excel sheet
   */
  exportToExcel(
    data: any[],
    columns: ColDef[],
    filename: string,
    sheetName: string = 'Sheet1'
  ): void {
    try {
      // Filter out columns that shouldn't be exported (e.g., action columns)
      const exportableColumns = columns.filter(col =>
        col.field && col.headerName && !col.cellRenderer
      );

      // Map data to include only exportable fields with proper headers
      const exportData = data.map(row => {
        const exportRow: any = {};
        exportableColumns.forEach(col => {
          const headerName = col.headerName || col.field!;
          let value = row[col.field!];

          // Format values
          if (value instanceof Date) {
            value = value.toISOString().split('T')[0];
          } else if (typeof value === 'boolean') {
            value = value ? 'Yes' : 'No';
          } else if (value === null || value === undefined) {
            value = '';
          }

          exportRow[headerName] = value;
        });
        return exportRow;
      });

      // Create worksheet from data
      const worksheet = XLSX.utils.json_to_sheet(exportData);

      // Auto-size columns
      const columnWidths = exportableColumns.map(col => ({
        wch: Math.max(
          (col.headerName || col.field || '').length,
          15 // Minimum width
        )
      }));
      worksheet['!cols'] = columnWidths;

      // Create workbook and add worksheet
      const workbook = XLSX.utils.book_new();
      XLSX.utils.book_append_sheet(workbook, worksheet, sheetName);

      // Write file
      XLSX.writeFile(workbook, `${filename}.xlsx`);
    } catch (error) {
      console.error('Error exporting to Excel:', error);
      throw new Error('Failed to export data to Excel');
    }
  }

  /**
   * Export selected rows from grid to Excel
   * @param gridApi - AG-Grid API instance
   * @param columns - Column definitions
   * @param filename - Name of the file (without extension)
   */
  exportSelectedToExcel(gridApi: GridApi, columns: ColDef[], filename: string): void {
    const selectedRows = gridApi.getSelectedRows();
    if (selectedRows.length === 0) {
      throw new Error('No rows selected for export');
    }
    this.exportToExcel(selectedRows, columns, `${filename}_selected`, 'Selected Data');
  }

  /**
   * Get all row data from grid (useful for infinite scroll grids)
   * @param gridApi - AG-Grid API instance
   * @returns Array of all row data
   */
  getAllRowData(gridApi: GridApi): any[] {
    const rowData: any[] = [];
    gridApi.forEachNode(node => {
      if (node.data) {
        rowData.push(node.data);
      }
    });
    return rowData;
  }
}
