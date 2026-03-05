import { Injectable, signal } from '@angular/core';
import { TauriService } from './tauri.service';
import { DatabaseConnection } from '../models';

@Injectable({ providedIn: 'root' })
export class ConnectionService {
  private _connections = signal<DatabaseConnection[]>([]);
  readonly connections = this._connections.asReadonly();

  constructor(private tauri: TauriService) {}

  async load(): Promise<void> {
    const connections = await this.tauri.getConnections();
    this._connections.set(connections);
  }

  async add(connection: DatabaseConnection): Promise<void> {
    await this.tauri.saveConnection(connection);
    await this.load();
  }

  async update(connection: DatabaseConnection): Promise<void> {
    await this.tauri.saveConnection(connection);
    await this.load();
  }

  async remove(id: string): Promise<void> {
    await this.tauri.deleteConnection(id);
    this._connections.update(list => list.filter(c => c.id !== id));
  }

  generateId(): string {
    return crypto.randomUUID();
  }
}
