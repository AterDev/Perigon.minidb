import { Injectable } from '@angular/core';
import { invoke } from '@tauri-apps/api/core';
import { DatabaseConnection, AppSettings, TableDataResult, FieldMeta, FilterRequest } from '../models';

/**
 * Service that wraps all Tauri invoke calls.
 * Provides a typed API to communicate with the Rust backend.
 */
@Injectable({ providedIn: 'root' })
export class TauriService {
  // ── Settings ──────────────────────────────────────────────────────────────

  getSettings(): Promise<AppSettings> {
    return invoke('get_settings');
  }

  saveSettings(settings: AppSettings): Promise<void> {
    return invoke('save_settings', { settings });
  }

  // ── Connections ───────────────────────────────────────────────────────────

  getConnections(): Promise<DatabaseConnection[]> {
    return invoke('get_connections');
  }

  saveConnection(connection: DatabaseConnection): Promise<void> {
    return invoke('save_connection', { connection });
  }

  deleteConnection(id: string): Promise<void> {
    return invoke('delete_connection', { id });
  }

  // ── Database ──────────────────────────────────────────────────────────────

  connectDatabase(connectionId: string, filePath: string): Promise<string[]> {
    return invoke('connect_database', { connectionId, filePath });
  }

  disconnectDatabase(): Promise<void> {
    return invoke('disconnect_database');
  }

  getFieldMetadata(tableName: string): Promise<FieldMeta[]> {
    return invoke('get_field_metadata', { tableName });
  }

  loadTableData(
    tableName: string,
    page: number,
    pageSize: number,
    filter?: FilterRequest
  ): Promise<TableDataResult> {
    return invoke('load_table_data', { tableName, page, pageSize, filter: filter ?? null });
  }

  refreshDatabase(): Promise<string[]> {
    return invoke('refresh_database');
  }

  openConnectionsManagerWindow(): Promise<void> {
    return invoke('open_connections_manager_window');
  }
}
