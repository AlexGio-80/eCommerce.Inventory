import { Injectable } from '@angular/core';
import { ColumnState } from 'ag-grid-community';

export interface GridState {
    columnState: ColumnState[];
    sortModel: any[];
    filterModel?: any;           // AG-Grid filter model
    quickFilterText?: string;    // Global search text
    scrollPosition?: number;     // Scroll position for restoration
}

@Injectable({
    providedIn: 'root'
})
export class GridStateService {

    /**
     * Save grid state to localStorage
     */
    saveGridState(gridId: string, state: GridState): void {
        try {
            localStorage.setItem(`grid-state-${gridId}`, JSON.stringify(state));
        } catch (error) {
            console.error('Error saving grid state:', error);
        }
    }

    /**
     * Load grid state from localStorage
     */
    loadGridState(gridId: string): GridState | null {
        try {
            const stateJson = localStorage.getItem(`grid-state-${gridId}`);
            return stateJson ? JSON.parse(stateJson) : null;
        } catch (error) {
            console.error('Error loading grid state:', error);
            return null;
        }
    }

    /**
     * Clear grid state from localStorage
     */
    clearGridState(gridId: string): void {
        try {
            localStorage.removeItem(`grid-state-${gridId}`);
        } catch (error) {
            console.error('Error clearing grid state:', error);
        }
    }

    /**
     * Save grid state for a specific tab
     */
    saveGridStateForTab(gridId: string, tabId: string, state: GridState): void {
        try {
            const key = `grid-state-${gridId}-tab-${tabId}`;
            localStorage.setItem(key, JSON.stringify(state));
        } catch (error) {
            console.error('Error saving grid state for tab:', error);
        }
    }

    /**
     * Load grid state for a specific tab
     */
    loadGridStateForTab(gridId: string, tabId: string): GridState | null {
        try {
            const key = `grid-state-${gridId}-tab-${tabId}`;
            const stateJson = localStorage.getItem(key);
            return stateJson ? JSON.parse(stateJson) : null;
        } catch (error) {
            console.error('Error loading grid state for tab:', error);
            return null;
        }
    }

    /**
     * Clear grid state for a specific tab
     */
    clearGridStateForTab(gridId: string, tabId: string): void {
        try {
            const key = `grid-state-${gridId}-tab-${tabId}`;
            localStorage.removeItem(key);
        } catch (error) {
            console.error('Error clearing grid state for tab:', error);
        }
    }
}
