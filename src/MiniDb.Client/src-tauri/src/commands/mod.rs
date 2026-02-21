use tauri::{Manager, State, WebviewUrl, WebviewWindowBuilder};
use crate::state::{AppState, DatabaseConnection, AppSettings};
use crate::mds::reader;
use crate::mds::types::FilterRequest;
use std::collections::HashMap;

// ── Settings commands ─────────────────────────────────────────────────────────

#[tauri::command]
pub async fn get_settings(state: State<'_, AppState>) -> Result<AppSettings, String> {
    let settings = state.settings.lock().unwrap();
    Ok(settings.clone())
}

#[tauri::command]
pub async fn save_settings(
    settings: AppSettings,
    state: State<'_, AppState>,
) -> Result<(), String> {
    let settings_path = format!("{}/settings.json", state.data_dir);
    let json = serde_json::to_string_pretty(&settings).map_err(|e| e.to_string())?;
    std::fs::write(&settings_path, json).map_err(|e| e.to_string())?;
    *state.settings.lock().unwrap() = settings;
    Ok(())
}

// ── Connection management commands ───────────────────────────────────────────

#[tauri::command]
pub async fn get_connections(state: State<'_, AppState>) -> Result<Vec<DatabaseConnection>, String> {
    let connections = state.connections.lock().unwrap();
    Ok(connections.clone())
}

#[tauri::command]
pub async fn save_connection(
    connection: DatabaseConnection,
    state: State<'_, AppState>,
) -> Result<(), String> {
    let mut connections = state.connections.lock().unwrap();
    if let Some(existing) = connections.iter_mut().find(|c| c.id == connection.id) {
        *existing = connection;
    } else {
        connections.push(connection);
    }
    save_connections_to_disk(&connections, &state.data_dir)
}

#[tauri::command]
pub async fn delete_connection(
    id: String,
    state: State<'_, AppState>,
) -> Result<(), String> {
    let mut connections = state.connections.lock().unwrap();
    connections.retain(|c| c.id != id);
    save_connections_to_disk(&connections, &state.data_dir)
}

fn save_connections_to_disk(
    connections: &[DatabaseConnection],
    data_dir: &str,
) -> Result<(), String> {
    let path = format!("{}/connections.json", data_dir);
    let json = serde_json::to_string_pretty(connections).map_err(|e| e.to_string())?;
    std::fs::write(path, json).map_err(|e| e.to_string())
}

// ── Database file commands ────────────────────────────────────────────────────

/// Open/connect to a MDS database file - loads table names and metadata into state
#[tauri::command]
pub async fn connect_database(
    connection_id: String,
    file_path: String,
    state: State<'_, AppState>,
) -> Result<Vec<String>, String> {
    let file_info = reader::read_file_info(&file_path)?;

    let mut table_metas = HashMap::new();
    let mut field_metas = HashMap::new();
    let mut table_names = Vec::new();

    for table in &file_info.tables {
        if table.name.is_empty() {
            continue;
        }
        let fields = reader::read_field_metadata(&file_path, table)?;
        table_names.push(table.name.clone());
        field_metas.insert(table.name.clone(), fields);
        table_metas.insert(table.name.clone(), table.clone());
    }

    table_names.sort();

    // Update connection last_connected_at
    {
        let mut connections = state.connections.lock().unwrap();
        if let Some(conn) = connections.iter_mut().find(|c| c.id == connection_id) {
            conn.last_connected_at = Some(chrono::Utc::now().to_rfc3339());
            conn.last_connection_error = None;
        }
        save_connections_to_disk(&connections, &state.data_dir).ok();
    }

    *state.active_connection.lock().unwrap() = Some(crate::state::ActiveConnection {
        connection_id,
        file_path,
        table_names: table_names.clone(),
        table_metas,
        field_metas,
    });

    Ok(table_names)
}

/// Disconnect from the current database
#[tauri::command]
pub async fn disconnect_database(state: State<'_, AppState>) -> Result<(), String> {
    *state.active_connection.lock().unwrap() = None;
    Ok(())
}

/// Get field metadata for a table
#[tauri::command]
pub async fn get_field_metadata(
    table_name: String,
    state: State<'_, AppState>,
) -> Result<Vec<crate::mds::types::FieldMeta>, String> {
    let active = state.active_connection.lock().unwrap();
    let conn = active.as_ref().ok_or("No active connection")?;
    let fields = conn
        .field_metas
        .get(&table_name)
        .ok_or_else(|| format!("Table '{}' not found", table_name))?;
    Ok(fields.clone())
}

/// Load table data with pagination and optional filtering
#[tauri::command]
pub async fn load_table_data(
    table_name: String,
    page: usize,
    page_size: usize,
    filter: Option<FilterRequest>,
    state: State<'_, AppState>,
) -> Result<crate::mds::types::TableDataResult, String> {
    let (file_path, table_meta, field_metas) = {
        let active = state.active_connection.lock().unwrap();
        let conn = active.as_ref().ok_or("No active connection")?;
        let table = conn
            .table_metas
            .get(&table_name)
            .ok_or_else(|| format!("Table '{}' not found", table_name))?
            .clone();
        let fields = conn
            .field_metas
            .get(&table_name)
            .ok_or_else(|| format!("Table '{}' fields not found", table_name))?
            .clone();
        (conn.file_path.clone(), table, fields)
    };

    reader::load_table_records(
        &file_path,
        &table_meta,
        &field_metas,
        page,
        page_size,
        filter.as_ref(),
    )
}

/// Refresh table data (reload from file)
#[tauri::command]
pub async fn refresh_database(
    state: State<'_, AppState>,
) -> Result<Vec<String>, String> {
    let (connection_id, file_path) = {
        let active = state.active_connection.lock().unwrap();
        let conn = active.as_ref().ok_or("No active connection")?;
        (conn.connection_id.clone(), conn.file_path.clone())
    };
    
    connect_database(connection_id, file_path, state).await
}

/// Open (or focus) the native connections manager window
#[tauri::command]
pub async fn open_connections_manager_window(app: tauri::AppHandle) -> Result<(), String> {
    if let Some(window) = app.get_webview_window("connections-manager") {
        window.show().map_err(|e| e.to_string())?;
        window.set_focus().map_err(|e| e.to_string())?;
        return Ok(());
    }

    let window = WebviewWindowBuilder::new(
        &app,
        "connections-manager",
        WebviewUrl::App("index.html".into()),
    )
    .title("Connection Manager")
    .inner_size(860.0, 620.0)
    .min_inner_size(640.0, 420.0)
    .center()
    .resizable(true)
    .decorations(false)
    .build()
    .map_err(|e| e.to_string())?;

    #[cfg(target_os = "windows")]
    {
        use window_vibrancy::{apply_blur, apply_mica};
        if apply_mica(&window, None).is_err() {
            let _ = apply_blur(&window, Some((24, 24, 24, 125)));
        }
    }

    Ok(())
}
