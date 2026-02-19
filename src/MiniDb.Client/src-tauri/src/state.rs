use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::sync::Mutex;
use crate::mds::types::{FieldMeta, TableMeta};

/// Persisted database connection
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DatabaseConnection {
    pub id: String,
    pub name: String,
    pub path: String,
    pub last_connected_at: Option<String>,
    pub last_connection_error: Option<String>,
}

/// App settings
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AppSettings {
    pub theme: String,          // "light" | "dark" | "system"
    pub language: String,       // "en" | "zh-CN"
    pub enable_mica: bool,      // Windows Mica effect
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            theme: "system".to_string(),
            language: "en".to_string(),
            enable_mica: true,
        }
    }
}

/// Active database connection state (loaded metadata)
#[derive(Debug, Clone)]
pub struct ActiveConnection {
    pub connection_id: String,
    pub file_path: String,
    #[allow(dead_code)]
    pub table_names: Vec<String>,
    pub table_metas: HashMap<String, TableMeta>,
    pub field_metas: HashMap<String, Vec<FieldMeta>>,
}

/// Global app state managed by Tauri
pub struct AppState {
    pub connections: Mutex<Vec<DatabaseConnection>>,
    pub settings: Mutex<AppSettings>,
    pub active_connection: Mutex<Option<ActiveConnection>>,
    pub data_dir: String,
}

impl AppState {
    pub fn new(data_dir: String) -> Self {
        Self {
            connections: Mutex::new(vec![]),
            settings: Mutex::new(AppSettings::default()),
            active_connection: Mutex::new(None),
            data_dir,
        }
    }
}
