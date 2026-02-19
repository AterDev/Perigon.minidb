import {
  Component,
  Input,
  OnInit,
  OnDestroy,
  OnChanges,
  SimpleChanges,
  signal,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';

// Angular Material
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDividerModule } from '@angular/material/divider';
import { MatChipsModule } from '@angular/material/chips';

import { TauriService } from '../../core/services/tauri.service';
import {
  FieldMeta,
  TableDataResult,
  FilterRequest,
  FilterOperator,
  FILTER_OPERATORS,
  RecordRow,
} from '../../core/models';

@Component({
  selector: 'app-database-page',
  standalone: true,
  imports: [
    CommonModule, FormsModule, TranslateModule,
    MatSidenavModule, MatListModule, MatButtonModule, MatIconModule,
    MatProgressSpinnerModule, MatTableModule, MatFormFieldModule,
    MatInputModule, MatSelectModule, MatTooltipModule, MatDividerModule,
    MatChipsModule,
  ],
  templateUrl: './database-page.component.html',
  styleUrl: './database-page.component.css',
})
export class DatabasePageComponent implements OnInit, OnDestroy, OnChanges {
  @Input() isConnected = false;
  @Input() connectedName = '';

  // ── State ──────────────────────────────────────────────────────────────────
  readonly tableNames = signal<string[]>([]);
  readonly selectedTable = signal<string>('');
  readonly tableData = signal<TableDataResult | null>(null);
  readonly fieldMetas = signal<FieldMeta[]>([]);
  readonly isLoading = signal(false);
  readonly errorMsg = signal('');
  readonly currentPage = signal(0);
  readonly pageSize = signal(50);
  readonly isRefreshing = signal(false);

  // ── Filter ─────────────────────────────────────────────────────────────────
  readonly filterField = signal('');
  readonly filterOperator = signal<FilterOperator>('contains');
  readonly filterValue = signal('');
  readonly filterValueTo = signal('');
  readonly isFilterActive = signal(false);
  readonly filterOpen = signal(false);
  readonly filterOperators = FILTER_OPERATORS;
  private refreshRequestHandler = () => {
    void this.refresh();
  };

  // ── Computed ───────────────────────────────────────────────────────────────
  readonly totalPages = computed(() => {
    const data = this.tableData();
    if (!data) return 0;
    return Math.ceil(data.totalCount / this.pageSize()) || 1;
  });

  readonly hasPrev = computed(() => this.currentPage() > 0);
  readonly hasNext = computed(() => this.currentPage() < this.totalPages() - 1);

  readonly activeFilter = computed<FilterRequest | undefined>(() => {
    if (this.isFilterActive() && this.filterField() && this.filterValue()) {
      return {
        field: this.filterField(),
        operator: this.filterOperator(),
        value: this.filterValue(),
        valueTo: this.filterOperator() === 'range' ? this.filterValueTo() : undefined,
      };
    }
    return undefined;
  });

  constructor(private tauri: TauriService) {}

  async ngOnInit(): Promise<void> {
    window.addEventListener('minidb:refresh-request', this.refreshRequestHandler);
    if (this.isConnected) {
      await this.refresh(/* initial */ true);
    }
  }

  async ngOnChanges(changes: SimpleChanges): Promise<void> {
    if (!changes['isConnected']) return;

    if (!this.isConnected) {
      this.resetView();
      return;
    }

    if (this.tableNames().length === 0) {
      await this.refresh(true);
    }
  }

  ngOnDestroy(): void {
    window.removeEventListener('minidb:refresh-request', this.refreshRequestHandler);
  }

  // ── Table selection ────────────────────────────────────────────────────────

  async selectTable(name: string): Promise<void> {
    if (!this.isConnected) return;
    if (this.selectedTable() === name) return;
    this.selectedTable.set(name);
    this.currentPage.set(0);
    this.clearFilterState();
    await this.loadData();
  }

  // ── Data loading ───────────────────────────────────────────────────────────

  async loadData(): Promise<void> {
    if (!this.isConnected) return;

    const table = this.selectedTable();
    if (!table) return;

    this.isLoading.set(true);
    this.errorMsg.set('');

    try {
      const [fields, data] = await Promise.all([
        this.tauri.getFieldMetadata(table),
        this.tauri.loadTableData(table, this.currentPage(), this.pageSize(), this.activeFilter()),
      ]);
      this.fieldMetas.set(fields);
      this.tableData.set(data);
    } catch (e: any) {
      this.errorMsg.set(`${e}`);
      this.tableData.set(null);
      this.emitStatus('error', 'status.loadFailed', { reason: `${e}` });
    } finally {
      this.isLoading.set(false);
    }
  }

  async refresh(initial = false): Promise<void> {
    if (!this.isConnected) {
      this.resetView();
      return;
    }

    this.isRefreshing.set(true);
    this.errorMsg.set('');
    try {
      const tables = await this.tauri.refreshDatabase();
      this.tableNames.set(tables);
      if (tables.length > 0) {
        const cur = this.selectedTable();
        const target = tables.includes(cur) ? cur : tables[0];
        this.selectedTable.set(target);
        this.currentPage.set(0);
        if (!initial || target) {
          await this.loadData();
        }
      } else {
        this.selectedTable.set('');
        this.tableData.set(null);
        this.fieldMetas.set([]);
      }
    } catch (e: any) {
      this.errorMsg.set(`${e}`);
      this.emitStatus('error', 'status.refreshFailed', { reason: `${e}` });
    } finally {
      this.isRefreshing.set(false);
    }
  }

  async disconnect(): Promise<void> {
    if (!this.isConnected) return;

    try {
      await this.tauri.disconnectDatabase();
      this.resetView();
      window.dispatchEvent(new CustomEvent('minidb:connection-state', {
        detail: { isConnected: false }
      }));
    } catch (e: any) {
      this.emitStatus('error', 'status.disconnectFailed', { reason: `${e}` });
    }
  }

  // ── Pagination ─────────────────────────────────────────────────────────────

  async prevPage(): Promise<void> {
    if (!this.hasPrev()) return;
    this.currentPage.update(p => p - 1);
    await this.loadData();
  }

  async nextPage(): Promise<void> {
    if (!this.hasNext()) return;
    this.currentPage.update(p => p + 1);
    await this.loadData();
  }

  async setPageSize(size: number): Promise<void> {
    if (!this.isConnected) return;
    this.pageSize.set(size);
    this.currentPage.set(0);
    await this.loadData();
  }

  // ── Filter ─────────────────────────────────────────────────────────────────
  toggleFilter(): void {
    if (!this.isConnected) return;
    this.filterOpen.update(v => !v);
  }

  async applyFilter(): Promise<void> {
    if (!this.isConnected) return;
    if (!this.filterField() || !this.filterValue()) return;
    this.isFilterActive.set(true);
    this.currentPage.set(0);
    this.filterOpen.set(false);
    await this.loadData();
  }

  async clearFilter(): Promise<void> {
    if (!this.isConnected) return;
    this.clearFilterState();
    await this.loadData();
  }

  private clearFilterState(): void {
    this.isFilterActive.set(false);
    this.filterField.set('');
    this.filterOperator.set('contains');
    this.filterValue.set('');
    this.filterValueTo.set('');
    this.filterOpen.set(false);
  }

  // ── Display helpers ────────────────────────────────────────────────────────

  get columns(): string[] {
    return this.tableData()?.fieldNames ?? [];
  }

  get records(): RecordRow[] {
    return this.tableData()?.records ?? [];
  }

  get totalCount(): number {
    return this.tableData()?.totalCount ?? 0;
  }

  cellValue(row: RecordRow, col: string): string {
    const v = row[col];
    return v !== undefined && v !== null ? v : '';
  }

  formatDisplayValue(value: string, col: string): string {
    if (!value) return '—';
    // Detect ISO date and shorten for display
    if (/^\d{4}-\d{2}-\d{2}T/.test(value)) {
      return value.replace('T', ' ').replace('.000Z', 'Z').substring(0, 19) + 'Z';
    }
    return value;
  }

  operatorNeedsValueTo(): boolean {
    return this.filterOperator() === 'range';
  }

  private resetView(): void {
    this.tableNames.set([]);
    this.selectedTable.set('');
    this.tableData.set(null);
    this.fieldMetas.set([]);
    this.errorMsg.set('');
    this.currentPage.set(0);
    this.isRefreshing.set(false);
    this.clearFilterState();
  }

  private emitStatus(level: 'info' | 'success' | 'error', key: string, params?: Record<string, unknown>): void {
    window.dispatchEvent(new CustomEvent('minidb:status', {
      detail: { level, key, params }
    }));
  }
}
