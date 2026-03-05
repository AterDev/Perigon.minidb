// MDS file format types
use serde::{Deserialize, Serialize};

/// File header magic: "MDB1"
pub const MAGIC: &[u8; 4] = b"MDB1";
pub const FILE_HEADER_SIZE: usize = 256;
pub const TABLE_META_SIZE: usize = 128;
#[allow(dead_code)]
pub const FIELD_META_SIZE: usize = 80;

/// Field type codes matching C# FieldTypeCode enum
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum FieldTypeCode {
    Unknown = 0,
    Int32 = 1,
    Boolean = 2,
    Decimal = 3,
    DateTime = 4,
    String = 5,
    Enum = 6,
}

impl From<i32> for FieldTypeCode {
    fn from(v: i32) -> Self {
        match v {
            1 => FieldTypeCode::Int32,
            2 => FieldTypeCode::Boolean,
            3 => FieldTypeCode::Decimal,
            4 => FieldTypeCode::DateTime,
            5 => FieldTypeCode::String,
            6 => FieldTypeCode::Enum,
            _ => FieldTypeCode::Unknown,
        }
    }
}

/// Table metadata as read from the .mds file
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TableMeta {
    pub name: String,
    pub record_count: i32,
    pub record_size: i32,
    pub data_start_offset: i64,
    pub reserved_record_count: i32,
    pub table_index: i32,
    pub extent_directory_offset: i64,
    pub extent_count: i32,
    pub field_metadata_offset: i64,
    pub field_count: i32,
}

/// Field metadata as read from the .mds file
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FieldMeta {
    pub name: String,
    pub type_code: FieldTypeCode,
    pub size: i32,
    pub is_nullable: bool,
}

/// A decoded record value (field name → string representation)
pub type RecordRow = std::collections::HashMap<String, String>;

/// Full table data response
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TableDataResult {
    pub table_name: String,
    pub field_names: Vec<String>,
    pub records: Vec<RecordRow>,
    pub total_count: usize,
    pub page: usize,
    pub page_size: usize,
    pub fallback_reason: Option<String>,
}

/// File info summary
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DbFileInfo {
    pub version: i16,
    pub table_count: i16,
    pub global_write_version: i64,
    pub tables: Vec<TableMeta>,
}

/// Filter request from frontend
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FilterRequest {
    pub field: String,
    pub operator: String,
    pub value: String,
    pub value_to: Option<String>,
}
