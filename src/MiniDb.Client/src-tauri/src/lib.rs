use tauri::Manager;
use std::fs;

mod mds;
mod state;
mod commands;

use state::AppState;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_fs::init())
        .plugin(tauri_plugin_shell::init())
        .setup(|app| {
            // Initialize data directory
            let data_dir = app
                .path()
                .app_data_dir()
                .expect("Failed to get app data dir")
                .to_string_lossy()
                .to_string();

            fs::create_dir_all(&data_dir).ok();

            let state = AppState::new(data_dir.clone());

            // Load persisted connections
            let connections_path = format!("{}/connections.json", data_dir);
            if let Ok(json) = fs::read_to_string(&connections_path) {
                if let Ok(connections) =
                    serde_json::from_str::<Vec<state::DatabaseConnection>>(&json)
                {
                    *state.connections.lock().unwrap() = connections;
                }
            }

            // Load persisted settings
            let settings_path = format!("{}/settings.json", data_dir);
            if let Ok(json) = fs::read_to_string(&settings_path) {
                if let Ok(settings) = serde_json::from_str::<state::AppSettings>(&json) {
                    *state.settings.lock().unwrap() = settings;
                }
            }

            app.manage(state);

            #[cfg(target_os = "windows")]
            {
                use window_vibrancy::{apply_blur, apply_mica};

                if let Some(window) = app.get_webview_window("main") {
                    if apply_mica(&window, None).is_err() {
                        let _ = apply_blur(&window, Some((24, 24, 24, 125)));
                    }
                }
            }

            // Open DevTools in dev mode to diagnose issues
            #[cfg(debug_assertions)]
            {
                let window = app.get_webview_window("main").unwrap();
                window.open_devtools();
            }

            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            commands::get_settings,
            commands::save_settings,
            commands::get_connections,
            commands::save_connection,
            commands::delete_connection,
            commands::connect_database,
            commands::disconnect_database,
            commands::get_field_metadata,
            commands::load_table_data,
            commands::refresh_database,
            commands::open_connections_manager_window,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
