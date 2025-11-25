import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';
import { Router } from '@angular/router';
import { GridState } from './grid-state.service';

export interface TabState {
    id: string;                    // UUID univoco
    title: string;                 // Titolo visualizzato
    route: string;                 // Route Angular
    icon?: string;                 // Icona Material
    gridState?: GridState;         // Stato griglia (da GridStateService)
    scrollPosition?: number;       // Posizione scroll
    customData?: any;              // Dati custom per il componente
    createdAt: Date;
    lastAccessedAt: Date;
}

const TAB_STORAGE_KEY = 'ecommerce-inventory-tabs';
const ACTIVE_TAB_STORAGE_KEY = 'ecommerce-inventory-active-tab';

@Injectable({
    providedIn: 'root'
})
export class TabManagerService {
    private tabsSubject = new BehaviorSubject<TabState[]>([]);
    private activeTabIdSubject = new BehaviorSubject<string | null>(null);

    public tabs$: Observable<TabState[]> = this.tabsSubject.asObservable();
    public activeTabId$: Observable<string | null> = this.activeTabIdSubject.asObservable();

    constructor(private router: Router) {
        this.loadTabs();
    }

    /**
     * Apre un nuovo tab o attiva uno esistente con la stessa route
     * @returns ID del tab aperto/attivato
     */
    openTab(route: string, title: string, icon?: string): string {
        const tabs = this.tabsSubject.value;

        // Cerca se esiste già un tab con questa route
        const existingTab = tabs.find(t => t.route === route);

        if (existingTab) {
            // Aggiorna lastAccessedAt e attiva il tab esistente
            existingTab.lastAccessedAt = new Date();
            this.tabsSubject.next([...tabs]);
            this.setActiveTab(existingTab.id);
            this.saveTabs();
            return existingTab.id;
        }

        // Crea nuovo tab
        const newTab: TabState = {
            id: this.generateUniqueId(),
            title,
            route,
            icon,
            createdAt: new Date(),
            lastAccessedAt: new Date()
        };

        const updatedTabs = [...tabs, newTab];
        this.tabsSubject.next(updatedTabs);
        this.setActiveTab(newTab.id);
        this.saveTabs();

        return newTab.id;
    }

    /**
     * Chiude un tab
     */
    closeTab(tabId: string): void {
        const tabs = this.tabsSubject.value;
        const tabIndex = tabs.findIndex(t => t.id === tabId);

        if (tabIndex === -1) return;

        const updatedTabs = tabs.filter(t => t.id !== tabId);
        this.tabsSubject.next(updatedTabs);

        // Se il tab chiuso era attivo, attiva un altro tab
        if (this.activeTabIdSubject.value === tabId) {
            if (updatedTabs.length > 0) {
                // Attiva il tab precedente o il primo disponibile
                const newActiveIndex = Math.max(0, tabIndex - 1);
                this.setActiveTab(updatedTabs[newActiveIndex].id);
                this.router.navigate([updatedTabs[newActiveIndex].route]);
            } else {
                this.activeTabIdSubject.next(null);
            }
        }

        this.saveTabs();
    }

    /**
     * Imposta il tab attivo
     */
    setActiveTab(tabId: string): void {
        const tabs = this.tabsSubject.value;
        const tab = tabs.find(t => t.id === tabId);

        if (tab) {
            tab.lastAccessedAt = new Date();
            this.activeTabIdSubject.next(tabId);
            this.tabsSubject.next([...tabs]);
            this.saveTabs();
        }
    }

    /**
     * Aggiorna lo stato di un tab
     */
    updateTabState(tabId: string, state: Partial<TabState>): void {
        const tabs = this.tabsSubject.value;
        const tab = tabs.find(t => t.id === tabId);

        if (tab) {
            Object.assign(tab, state);
            this.tabsSubject.next([...tabs]);
            this.saveTabs();
        }
    }

    /**
     * Riordina i tab (per drag-and-drop)
     */
    reorderTabs(fromIndex: number, toIndex: number): void {
        const tabs = [...this.tabsSubject.value];
        const [movedTab] = tabs.splice(fromIndex, 1);
        tabs.splice(toIndex, 0, movedTab);

        this.tabsSubject.next(tabs);
        this.saveTabs();
    }

    /**
     * Chiude tutti i tab
     */
    closeAllTabs(): void {
        this.tabsSubject.next([]);
        this.activeTabIdSubject.next(null);
        this.saveTabs();
    }

    /**
     * Chiude tutti i tab eccetto quello specificato
     */
    closeOtherTabs(tabId: string): void {
        const tabs = this.tabsSubject.value;
        const tabToKeep = tabs.find(t => t.id === tabId);

        if (tabToKeep) {
            this.tabsSubject.next([tabToKeep]);
            this.setActiveTab(tabId);
            this.saveTabs();
        }
    }

    /**
     * Chiude tutti i tab a destra di quello specificato
     */
    closeTabsToRight(tabId: string): void {
        const tabs = this.tabsSubject.value;
        const tabIndex = tabs.findIndex(t => t.id === tabId);

        if (tabIndex !== -1) {
            const updatedTabs = tabs.slice(0, tabIndex + 1);
            this.tabsSubject.next(updatedTabs);

            // Se il tab attivo era tra quelli chiusi, attiva l'ultimo rimasto
            const activeTabId = this.activeTabIdSubject.value;
            if (activeTabId && !updatedTabs.find(t => t.id === activeTabId)) {
                this.setActiveTab(updatedTabs[updatedTabs.length - 1].id);
            }

            this.saveTabs();
        }
    }

    /**
     * Ottiene il tab attivo corrente
     */
    getActiveTab(): TabState | null {
        const activeId = this.activeTabIdSubject.value;
        if (!activeId) return null;

        return this.tabsSubject.value.find(t => t.id === activeId) || null;
    }

    /**
     * Ottiene un tab per ID
     */
    getTab(tabId: string): TabState | null {
        return this.tabsSubject.value.find(t => t.id === tabId) || null;
    }

    /**
     * Salva i tab in localStorage
     */
    saveTabs(): void {
        try {
            const tabs = this.tabsSubject.value;
            const activeTabId = this.activeTabIdSubject.value;

            localStorage.setItem(TAB_STORAGE_KEY, JSON.stringify(tabs));
            if (activeTabId) {
                localStorage.setItem(ACTIVE_TAB_STORAGE_KEY, activeTabId);
            } else {
                localStorage.removeItem(ACTIVE_TAB_STORAGE_KEY);
            }
        } catch (error) {
            console.error('Error saving tabs to localStorage:', error);
        }
    }

    /**
     * Carica i tab da localStorage
     */
    loadTabs(): void {
        try {
            const tabsJson = localStorage.getItem(TAB_STORAGE_KEY);
            const activeTabId = localStorage.getItem(ACTIVE_TAB_STORAGE_KEY);

            if (tabsJson) {
                const tabs: TabState[] = JSON.parse(tabsJson);

                // Converti le date da string a Date
                tabs.forEach(tab => {
                    tab.createdAt = new Date(tab.createdAt);
                    tab.lastAccessedAt = new Date(tab.lastAccessedAt);
                });

                this.tabsSubject.next(tabs);

                if (activeTabId && tabs.find(t => t.id === activeTabId)) {
                    this.activeTabIdSubject.next(activeTabId);
                } else if (tabs.length > 0) {
                    this.activeTabIdSubject.next(tabs[0].id);
                }
            }
        } catch (error) {
            console.error('Error loading tabs from localStorage:', error);
        }
    }

    /**
     * Genera un ID univoco per i tab
     */
    private generateUniqueId(): string {
        return `tab-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
    }
}
