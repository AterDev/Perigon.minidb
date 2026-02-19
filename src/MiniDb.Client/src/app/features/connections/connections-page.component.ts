import { Component, signal, Output, EventEmitter, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';

// Angular Material
import { MatListModule } from '@angular/material/list';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslateService } from '@ngx-translate/core';

import { ConnectionService } from '../../core/services/connection.service';
import { TauriService } from '../../core/services/tauri.service';
import { DatabaseConnection } from '../../core/models';
import { open } from '@tauri-apps/plugin-dialog';

interface ConnectionState {
  isConnected: boolean;
  id?: string;
  name: string;
}

@Component({
  selector: 'app-connections-page',
  standalone: true,
  imports: [
    CommonModule, FormsModule, TranslateModule,
    MatListModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule,
    MatTooltipModule, MatProgressSpinnerModule, MatSnackBarModule,
  ],
  templateUrl: './connections-page.component.html',
  styleUrl: './connections-page.component.css',
})
export class ConnectionsPageComponent {
  @Output() connected = new EventEmitter<ConnectionState>();
  @Input() isConnected = false;
  @Input() connectedId = '';
  @Input() connectedName = '';

  readonly isDialogOpen = signal(false);
  readonly isEditing = signal(false);
  readonly dialogName = signal('');
  readonly dialogPath = signal('');
  readonly editingId = signal('');
  readonly connectingId = signal('');

  constructor(
    public connectionService: ConnectionService,
    private tauri: TauriService,
    private snackBar: MatSnackBar,
    private translate: TranslateService,
  ) {}

  openAddDialog(): void {
    this.isEditing.set(false);
    this.dialogName.set('');
    this.dialogPath.set('');
    this.editingId.set('');
    this.isDialogOpen.set(true);
  }

  openEditDialog(conn: DatabaseConnection): void {
    this.isEditing.set(true);
    this.dialogName.set(conn.name);
    this.dialogPath.set(conn.path);
    this.editingId.set(conn.id);
    this.isDialogOpen.set(true);
  }

  closeDialog(): void {
    this.isDialogOpen.set(false);
  }

  async browseFile(): Promise<void> {
    try {
      const selected = await open({
        multiple: false,
        filters: [{ name: 'MiniDb Files', extensions: ['mds'] }],
      });
      if (selected && typeof selected === 'string') {
        this.dialogPath.set(selected);
      }
    } catch (e) {
      console.error('File browser error:', e);
    }
  }

  async saveConnection(): Promise<void> {
    const name = this.dialogName().trim();
    const path = this.dialogPath().trim();
    if (!name || !path) return;

    const conn: DatabaseConnection = {
      id: this.isEditing() ? this.editingId() : this.connectionService.generateId(),
      name,
      path,
    };

    try {
      if (this.isEditing()) {
        await this.connectionService.update(conn);
      } else {
        await this.connectionService.add(conn);
      }
      this.closeDialog();
    } catch (e: any) {
      this.snackBar.open(`Error: ${e}`, 'OK', { duration: 4000 });
    }
  }

  async deleteConnection(conn: DatabaseConnection): Promise<void> {
    if (!confirm(this.translate.instant('connection.deleteConfirmNamed', { name: conn.name }))) return;
    await this.connectionService.remove(conn.id);
  }

  async connect(conn: DatabaseConnection): Promise<void> {
    this.connectingId.set(conn.id);
    this.emitStatus('info', 'status.connecting', { name: conn.name });
    try {
      await this.tauri.connectDatabase(conn.id, conn.path);
      window.dispatchEvent(new CustomEvent('minidb:connection-state', {
        detail: { isConnected: true, id: conn.id, name: conn.name }
      }));
      this.connected.emit({ isConnected: true, id: conn.id, name: conn.name });
    } catch (e: any) {
      this.emitStatus('error', 'status.connectFailed', { reason: `${e}` });
      this.snackBar.open(this.translate.instant('error.connectFailed'), this.translate.instant('common.ok'), {
        duration: 5000,
        panelClass: 'snack-error'
      });
    } finally {
      this.connectingId.set('');
    }
  }

  isActive(conn: DatabaseConnection): boolean {
    return this.isConnected && this.connectedId === conn.id;
  }

  get connections() {
    return this.connectionService.connections();
  }

  private emitStatus(level: 'info' | 'success' | 'error', key: string, params?: Record<string, unknown>): void {
    window.dispatchEvent(new CustomEvent('minidb:status', {
      detail: { level, key, params }
    }));
  }
}
