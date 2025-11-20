import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSortModule } from '@angular/material/sort';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Observable, BehaviorSubject, Subject } from 'rxjs';
import { map, tap, switchMap, startWith, debounceTime, distinctUntilChanged } from 'rxjs/operators';

import { BlueprintsService } from '../../../../core/services/blueprints.service';
import { CardTraderApiService } from '../../../../core/services';
import { Blueprint, Game, Expansion, PagedResponse } from '../../../../core/models';

/**
 * Component for displaying and managing blueprints (cards)
 * Supports pagination, filtering by game/expansion, and searching by name
 */
@Component({
  selector: 'app-blueprints-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatTableModule,
    MatPaginatorModule,
    MatSortModule,
    MatFormFieldModule,
    MatSelectModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatToolbarModule,
    MatCardModule,
    MatChipsModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './blueprints-list.component.html',
  styleUrls: ['./blueprints-list.component.scss'],
})
export class BlueprintsListComponent implements OnInit {
  displayedColumns: string[] = [
    'id',
    'name',
    'game',
    'expansion',
    'rarity',
    'version',
    'actions',
  ];

  blueprints$!: Observable<Blueprint[]>;
  games$!: Observable<Game[]>;
  expansions$!: Observable<Expansion[]>;
  totalBlueprints = 0;
  isLoading = true;

  pageSize = 20;
  pageSizeOptions = [10, 20, 50, 100];
  currentPage = 0;

  selectedGame: Game | null = null;
  selectedExpansion: Expansion | null = null;
  searchTerm = '';

  private pageSubject = new BehaviorSubject<number>(1);
  private searchSubject = new Subject<string>();

  constructor(
    private blueprintsService: BlueprintsService,
    private apiService: CardTraderApiService
  ) { }

  ngOnInit(): void {
    this.games$ = this.apiService.getGames();

    // Setup search with debounce
    this.setupSearch();

    // Load blueprints
    this.loadBlueprints();
  }

  /**
   * Setup search functionality with debounce
   */
  private setupSearch(): void {
    this.searchSubject
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        tap((term) => {
          this.searchTerm = term;
          this.pageSubject.next(1); // Reset to first page on search
        })
      )
      .subscribe();
  }

  /**
   * Load blueprints with pagination
   */
  private loadBlueprints(): void {
    this.blueprints$ = this.pageSubject.pipe(
      tap(() => {
        this.isLoading = true;
      }),
      switchMap((page) => {
        if (this.searchTerm) {
          return this.blueprintsService.searchBlueprints(this.searchTerm);
        } else if (this.selectedExpansion) {
          return this.blueprintsService.getBlueprintsByExpansion(this.selectedExpansion.id);
        } else if (this.selectedGame) {
          return this.blueprintsService.getBlueprintsByGame(this.selectedGame.id);
        } else {
          return this.blueprintsService.getAllBlueprints(page, this.pageSize).pipe(
            tap((response: PagedResponse<Blueprint>) => {
              this.totalBlueprints = response.totalCount || 0;
            }),
            map((response: PagedResponse<Blueprint>) => response.items)
          );
        }
      }),
      tap(() => {
        this.isLoading = false;
      })
    );
  }

  /**
   * Handle game selection
   */
  onGameSelected(game: Game | null): void {
    this.selectedGame = game;
    this.selectedExpansion = null;
    if (game) {
      this.expansions$ = this.apiService.getExpansions(game.id);
    }
    this.pageSubject.next(1);
    this.loadBlueprints();
  }

  /**
   * Handle expansion selection
   */
  onExpansionSelected(expansion: Expansion | null): void {
    this.selectedExpansion = expansion;
    this.pageSubject.next(1);
    this.loadBlueprints();
  }

  /**
   * Handle search input
   */
  onSearch(term: string): void {
    this.searchSubject.next(term);
    this.loadBlueprints();
  }

  /**
   * Handle pagination changes
   */
  onPageChange(event: PageEvent): void {
    const pageNumber = event.pageIndex + 1;
    this.pageSize = event.pageSize;
    this.pageSubject.next(pageNumber);
  }

  /**
   * View blueprint details
   */
  viewBlueprint(blueprint: Blueprint): void {
    console.log('View blueprint:', blueprint);
    // TODO: Implement navigation to detail view
  }

  /**
   * Copy blueprint ID to clipboard
   */
  copyToClipboard(id: number): void {
    navigator.clipboard.writeText(id.toString());
    // TODO: Show toast notification
  }

  /**
   * Get rarity color for chip
   */
  getRarityColor(rarity: string | null): string {
    if (!rarity) return '';
    switch (rarity.toLowerCase()) {
      case 'mythic':
        return 'warn';
      case 'rare':
        return 'primary';
      case 'uncommon':
        return 'accent';
      case 'common':
        return '';
      default:
        return '';
    }
  }
}
