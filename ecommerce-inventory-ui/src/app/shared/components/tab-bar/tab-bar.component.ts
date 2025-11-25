import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDividerModule } from '@angular/material/divider';
import { CdkDragDrop, DragDropModule } from '@angular/cdk/drag-drop';
import { TabManagerService, TabState } from '../../../core/services';
import { Observable } from 'rxjs';

@Component({
    selector: 'app-tab-bar',
    standalone: true,
    imports: [
        CommonModule,
        MatButtonModule,
        MatIconModule,
        MatMenuModule,
        MatTooltipModule,
        MatDividerModule,
        DragDropModule
    ],
    templateUrl: './tab-bar.component.html',
    styleUrls: ['./tab-bar.component.scss']
})
export class TabBarComponent implements OnInit {
    tabs$: Observable<TabState[]>;
    activeTabId$: Observable<string | null>;

    constructor(
        private tabManager: TabManagerService,
        private router: Router
    ) {
        this.tabs$ = this.tabManager.tabs$;
        this.activeTabId$ = this.tabManager.activeTabId$;
    }

    ngOnInit(): void {
        // Nessuna inizializzazione necessaria
    }

    /**
     * Gestisce il click su un tab
     */
    onTabClick(tab: TabState): void {
        this.tabManager.setActiveTab(tab.id);
        this.router.navigate([tab.route]);
    }

    /**
     * Gestisce il menu contestuale (click destro)
     */
    onContextMenu(event: MouseEvent, tab: TabState, tabs: TabState[], menuTrigger: any): void {
        event.preventDefault();
        menuTrigger.menuData = { tab, tabs };
        menuTrigger.openMenu();
    }

    /**
     * Gestisce la chiusura di un tab
     */
    onTabClose(tabId: string, event: Event): void {
        event.stopPropagation();
        this.tabManager.closeTab(tabId);
    }

    /**
     * Gestisce il drag-and-drop per riordinare i tab
     */
    onTabDrop(event: CdkDragDrop<TabState[]>): void {
        if (event.previousIndex !== event.currentIndex) {
            this.tabManager.reorderTabs(event.previousIndex, event.currentIndex);
        }
    }

    /**
     * Chiude tutti i tab
     */
    closeAllTabs(): void {
        this.tabManager.closeAllTabs();
    }

    /**
     * Chiude tutti i tab eccetto quello specificato
     */
    closeOtherTabs(tabId: string): void {
        this.tabManager.closeOtherTabs(tabId);
    }

    /**
     * Chiude tutti i tab a destra di quello specificato
     */
    closeTabsToRight(tabId: string): void {
        this.tabManager.closeTabsToRight(tabId);
    }

    /**
     * Verifica se un tab è attivo
     */
    isActive(tabId: string, activeTabId: string | null): boolean {
        return tabId === activeTabId;
    }

    /**
     * Ottiene l'indice di un tab
     */
    getTabIndex(tabs: TabState[], tabId: string): number {
        return tabs.findIndex(t => t.id === tabId);
    }

    /**
     * Verifica se ci sono tab a destra di quello specificato
     */
    hasTabsToRight(tabs: TabState[], tabId: string): boolean {
        const index = this.getTabIndex(tabs, tabId);
        return index !== -1 && index < tabs.length - 1;
    }
}
