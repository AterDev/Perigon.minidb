// MDS binary file reader
use std::fs::File;
use std::io::{Read, Seek, SeekFrom};
use byteorder::{LittleEndian, ReadBytesExt};
use crate::mds::types::*;

const EXTENT_RECORD_GROWTH: usize = 1000;

/// Read a fixed-length UTF-8 string from a buffer, stripping null bytes
fn read_fixed_string(buf: &[u8]) -> String {
    let end = buf.iter().position(|&b| b == 0).unwrap_or(buf.len());
    String::from_utf8_lossy(&buf[..end]).trim().to_string()
}

/// Read the file header and table metadata
pub fn read_file_info(path: &str) -> Result<DbFileInfo, String> {
    let mut f = File::open(path).map_err(|e| format!("Cannot open file: {}", e))?;

    // Read magic bytes
    let mut magic = [0u8; 4];
    f.read_exact(&mut magic).map_err(|e| e.to_string())?;
    if &magic != MAGIC {
        return Err(format!(
            "Invalid file format: magic bytes are {:?}, expected 'MDB1'",
            magic
        ));
    }

    let version = f.read_i16::<LittleEndian>().map_err(|e| e.to_string())?;
    let table_count = f.read_i16::<LittleEndian>().map_err(|e| e.to_string())?;
    let global_write_version = f.read_i64::<LittleEndian>().map_err(|e| e.to_string())?;

    // Skip remaining header bytes up to FILE_HEADER_SIZE
    f.seek(SeekFrom::Start(FILE_HEADER_SIZE as u64))
        .map_err(|e| e.to_string())?;

    let mut tables = Vec::with_capacity(table_count as usize);
    for _ in 0..table_count {
        let mut name_buf = [0u8; 64];
        f.read_exact(&mut name_buf).map_err(|e| e.to_string())?;
        let name = read_fixed_string(&name_buf);

        let record_count = f.read_i32::<LittleEndian>().map_err(|e| e.to_string())?;
        let record_size = f.read_i32::<LittleEndian>().map_err(|e| e.to_string())?;
        let data_start_offset = f.read_i64::<LittleEndian>().map_err(|e| e.to_string())?;
        let reserved_record_count = f.read_i32::<LittleEndian>().map_err(|e| e.to_string())?;
        let table_index = f.read_i32::<LittleEndian>().map_err(|e| e.to_string())?;
        let extent_directory_offset = f.read_i64::<LittleEndian>().map_err(|e| e.to_string())?;
        let extent_count = f.read_i32::<LittleEndian>().map_err(|e| e.to_string())?;
        let field_metadata_offset = f.read_i64::<LittleEndian>().map_err(|e| e.to_string())?;
        let field_count = f.read_i32::<LittleEndian>().map_err(|e| e.to_string())?;

        // Skip reserved bytes (TABLE_META_SIZE - 112 bytes = 16 bytes)
        let mut reserved = [0u8; 16];
        f.read_exact(&mut reserved).map_err(|e| e.to_string())?;

        tables.push(TableMeta {
            name,
            record_count,
            record_size,
            data_start_offset,
            reserved_record_count,
            table_index,
            extent_directory_offset,
            extent_count,
            field_metadata_offset,
            field_count,
        });
    }

    Ok(DbFileInfo {
        version,
        table_count,
        global_write_version,
        tables,
    })
}

/// Read field metadata for a specific table
pub fn read_field_metadata(path: &str, table: &TableMeta) -> Result<Vec<FieldMeta>, String> {
    if table.field_count <= 0 || table.field_metadata_offset <= 0 {
        return Ok(vec![]);
    }

    let mut f = File::open(path).map_err(|e| format!("Cannot open file: {}", e))?;
    f.seek(SeekFrom::Start(table.field_metadata_offset as u64))
        .map_err(|e| e.to_string())?;

    let mut fields = Vec::with_capacity(table.field_count as usize);
    for _ in 0..table.field_count {
        let mut name_buf = [0u8; 64];
        f.read_exact(&mut name_buf).map_err(|e| e.to_string())?;
        let name = read_fixed_string(&name_buf);

        let type_code_raw = f.read_i32::<LittleEndian>().map_err(|e| e.to_string())?;
        let size = f.read_i32::<LittleEndian>().map_err(|e| e.to_string())?;
        let is_nullable = f.read_u8().map_err(|e| e.to_string())? != 0;

        // Skip reserved bytes (FIELD_META_SIZE - 73 bytes = 7 bytes)
        let mut _reserved = [0u8; 7];
        f.read_exact(&mut _reserved).map_err(|e| e.to_string())?;

        fields.push(FieldMeta {
            name,
            type_code: FieldTypeCode::from(type_code_raw),
            size,
            is_nullable,
        });
    }

    Ok(fields)
}

/// Read extent start offsets from the extent directory
fn read_extent_starts(f: &mut File, table: &TableMeta) -> Result<Vec<i64>, String> {
    if table.extent_count <= 1 || table.extent_directory_offset <= 0 {
        return Ok(vec![table.data_start_offset]);
    }
    f.seek(SeekFrom::Start(table.extent_directory_offset as u64))
        .map_err(|e| e.to_string())?;
    let persisted_count = f.read_i32::<LittleEndian>().map_err(|e| e.to_string())?;
    let mut starts = Vec::with_capacity(persisted_count as usize);
    for _ in 0..persisted_count {
        starts.push(f.read_i64::<LittleEndian>().map_err(|e| e.to_string())?);
    }
    Ok(starts)
}

/// Calculate the file offset of a record (0-indexed) accounting for multi-extent layout.
/// Mirrors C#'s GetRecordOffset / GetExtentCapacities logic exactly.
fn get_record_offset(table: &TableMeta, extent_starts: &[i64], index: usize) -> Result<u64, String> {
    let n = extent_starts.len();
    if n == 1 {
        let offset = extent_starts[0] as u64 + (index as u64) * (table.record_size as u64);
        return Ok(offset);
    }
    // Multi-extent: first extent capacity = reserved - (n-1)*1000, rest = 1000
    let reserved = table.reserved_record_count as usize;
    let first_cap = {
        let raw = reserved.saturating_sub((n - 1) * EXTENT_RECORD_GROWTH);
        raw.max(EXTENT_RECORD_GROWTH)
    };
    let mut running = 0usize;
    for (i, &start) in extent_starts.iter().enumerate() {
        let cap = if i == 0 { first_cap } else { EXTENT_RECORD_GROWTH };
        if index < running + cap {
            let offset_in_extent = index - running;
            return Ok(start as u64 + (offset_in_extent as u64) * (table.record_size as u64));
        }
        running += cap;
    }
    Err(format!("Cannot map record index {} to any extent for table '{}'", index, table.name))
}

/// Load records from a table with pagination and optional filtering
pub fn load_table_records(
    path: &str,
    table: &TableMeta,
    fields: &[FieldMeta],
    page: usize,
    page_size: usize,
    filter: Option<&FilterRequest>,
) -> Result<TableDataResult, String> {
    let field_names: Vec<String> = fields.iter().map(|f| f.name.clone()).collect();

    if table.record_size <= 0 || table.record_count <= 0 {
        return Ok(TableDataResult {
            table_name: table.name.clone(),
            field_names,
            records: vec![],
            total_count: 0,
            page,
            page_size,
            fallback_reason: None,
        });
    }

    let mut f = File::open(path).map_err(|e| format!("Cannot open file: {}", e))?;

    // Load extent starts for multi-extent support
    let extent_starts = read_extent_starts(&mut f, table)?;

    // Collect all live (non-deleted) records that pass the filter
    let mut all_records: Vec<RecordRow> = Vec::new();

    for i in 0..(table.record_count as usize) {
        let record_offset = get_record_offset(table, &extent_starts, i)?;
        f.seek(SeekFrom::Start(record_offset))
            .map_err(|e| e.to_string())?;

        let is_deleted = f.read_u8().map_err(|e| e.to_string())?;
        if is_deleted != 0 {
            continue;
        }

        let id = f.read_i32::<LittleEndian>().map_err(|e| e.to_string())?;

        let mut row = RecordRow::new();
        row.insert("Id".to_string(), id.to_string());

        // Read each field
        for field in fields {
            let value = decode_field(&mut f, field)?;
            row.insert(field.name.clone(), value);
        }

        // Apply filter if provided
        if let Some(flt) = filter {
            if !apply_filter(&row, flt) {
                continue;
            }
        }

        all_records.push(row);
    }

    let total_count = all_records.len();

    // Apply pagination
    let start = page * page_size;
    let end = (start + page_size).min(total_count);
    let paged = if start < total_count {
        all_records[start..end].to_vec()
    } else {
        vec![]
    };

    // Build ordered field names: Id first, then rest alphabetically
    let mut ordered_names = vec!["Id".to_string()];
    ordered_names.extend(field_names);

    Ok(TableDataResult {
        table_name: table.name.clone(),
        field_names: ordered_names,
        records: paged,
        total_count,
        page,
        page_size,
        fallback_reason: None,
    })
}

/// Decode a single field value from the current file position
fn decode_field(f: &mut File, field: &FieldMeta) -> Result<String, String> {
    // Handle nullable: read null marker byte first
    if field.is_nullable {
        let is_null = f.read_u8().map_err(|e| e.to_string())? != 0;
        if is_null {
            // Still need to read/skip the rest of the value bytes
            let value_size = (field.size - 1) as usize;
            let mut skip = vec![0u8; value_size];
            f.read_exact(&mut skip).map_err(|e| e.to_string())?;
            return Ok(String::new());
        }
    }

    match field.type_code {
        FieldTypeCode::Int32 | FieldTypeCode::Enum => {
            let v = f.read_i32::<LittleEndian>().map_err(|e| e.to_string())?;
            Ok(v.to_string())
        }
        FieldTypeCode::Boolean => {
            let v = f.read_u8().map_err(|e| e.to_string())?;
            Ok(if v != 0 { "true" } else { "false" }.to_string())
        }
        FieldTypeCode::Decimal => {
            // .NET decimal is 16 bytes: 4 ints
            let lo = f.read_u32::<LittleEndian>().map_err(|e| e.to_string())?;
            let mid = f.read_u32::<LittleEndian>().map_err(|e| e.to_string())?;
            let hi = f.read_u32::<LittleEndian>().map_err(|e| e.to_string())?;
            let flags = f.read_u32::<LittleEndian>().map_err(|e| e.to_string())?;
            let decimal_val = decode_dotnet_decimal(lo, mid, hi, flags);
            Ok(decimal_val)
        }
        FieldTypeCode::DateTime => {
            // .NET DateTime ticks (int64), UTC
            let ticks = f.read_i64::<LittleEndian>().map_err(|e| e.to_string())?;
            let dt = ticks_to_iso8601(ticks);
            Ok(dt)
        }
        FieldTypeCode::String => {
            // C# stores strings as null-terminated UTF-8 in a fixed-size buffer.
            // field.size = MaxLength bytes (does NOT add a nullable byte for strings).
            // If nullable and not-null, 1 byte has already been consumed above.
            let data_size = (field.size - if field.is_nullable { 1 } else { 0 }) as usize;
            let mut buf = vec![0u8; data_size];
            f.read_exact(&mut buf).map_err(|e| e.to_string())?;
            let end = buf.iter().position(|&b| b == 0).unwrap_or(data_size);
            Ok(String::from_utf8_lossy(&buf[..end]).to_string())
        }
        FieldTypeCode::Unknown => {
            let size = (field.size - if field.is_nullable { 1 } else { 0 }) as usize;
            let mut bytes = vec![0u8; size];
            f.read_exact(&mut bytes).map_err(|e| e.to_string())?;
            Ok(format!("0x{}", hex::encode(&bytes)))
        }
    }
}

/// Decode .NET decimal (16 bytes: lo, mid, hi, flags)
fn decode_dotnet_decimal(lo: u32, mid: u32, hi: u32, flags: u32) -> String {
    let negative = (flags & 0x80000000) != 0;
    let scale = ((flags >> 16) & 0xFF) as u32;

    // Reconstruct mantissa as 96-bit integer
    let hi128 = hi as u128;
    let mid128 = mid as u128;
    let lo128 = lo as u128;
    let mantissa = (hi128 << 64) | (mid128 << 32) | lo128;

    // Divide by 10^scale
    let divisor = 10u128.pow(scale);
    let integer_part = mantissa / divisor;
    let frac_part = mantissa % divisor;

    let result = if scale == 0 {
        integer_part.to_string()
    } else {
        format!("{}.{:0>width$}", integer_part, frac_part, width = scale as usize)
    };

    if negative { format!("-{}", result) } else { result }
}

/// Convert .NET DateTime ticks to ISO 8601 string
/// .NET ticks: 100-nanosecond intervals since 0001-01-01T00:00:00
fn ticks_to_iso8601(ticks: i64) -> String {
    if ticks <= 0 {
        return String::new();
    }
    // Unix epoch is 1970-01-01 = 621355968000000000 ticks from .NET epoch
    const EPOCH_TICKS: i64 = 621_355_968_000_000_000;
    let unix_100ns = ticks - EPOCH_TICKS;
    if unix_100ns < 0 {
        // Before Unix epoch - format as date string manually
        return format!("ticks:{}", ticks);
    }
    let unix_millis = unix_100ns / 10_000;
    let secs = unix_millis / 1000;
    let millis = unix_millis % 1000;

    // Simple UTC date formatting without external crate
    format_unix_timestamp(secs, millis as u32)
}

/// Simple Unix timestamp to ISO 8601 UTC string
fn format_unix_timestamp(secs: i64, millis: u32) -> String {
    // Days since epoch
    let remaining_secs = secs;
    let time_of_day_secs = remaining_secs % 86400;
    let days_since_epoch = remaining_secs / 86400;

    let hours = (time_of_day_secs / 3600) as u32;
    let minutes = ((time_of_day_secs % 3600) / 60) as u32;
    let secs_rem = (time_of_day_secs % 60) as u32;

    // Compute year/month/day from days since Unix epoch (1970-01-01)
    let (year, month, day) = days_to_ymd(days_since_epoch);

    if millis > 0 {
        format!(
            "{:04}-{:02}-{:02}T{:02}:{:02}:{:02}.{:03}Z",
            year, month, day, hours, minutes, secs_rem, millis
        )
    } else {
        format!(
            "{:04}-{:02}-{:02}T{:02}:{:02}:{:02}Z",
            year, month, day, hours, minutes, secs_rem
        )
    }
}

fn days_to_ymd(days: i64) -> (i32, u32, u32) {
    // Algorithm: convert days since 1970-01-01 to (year, month, day)
    let z = days + 719468;
    let era = (if z >= 0 { z } else { z - 146096 }) / 146097;
    let doe = (z - era * 146097) as u32;
    let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
    let y = yoe as i32 + era as i32 * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let y = if m <= 2 { y + 1 } else { y };
    (y, m, d)
}

/// Apply a filter condition to a record
fn apply_filter(row: &RecordRow, filter: &FilterRequest) -> bool {
    let value = match row.get(&filter.field) {
        Some(v) => v.to_lowercase(),
        None => return true,
    };
    let filter_val = filter.value.to_lowercase();

    match filter.operator.as_str() {
        "contains" => value.contains(&filter_val),
        "equals" => value == filter_val,
        "starts_with" => value.starts_with(&filter_val),
        "ends_with" => value.ends_with(&filter_val),
        "gt" => {
            let a: f64 = value.parse().unwrap_or(0.0);
            let b: f64 = filter_val.parse().unwrap_or(0.0);
            a > b
        }
        "lt" => {
            let a: f64 = value.parse().unwrap_or(0.0);
            let b: f64 = filter_val.parse().unwrap_or(0.0);
            a < b
        }
        "gte" => {
            let a: f64 = value.parse().unwrap_or(0.0);
            let b: f64 = filter_val.parse().unwrap_or(0.0);
            a >= b
        }
        "lte" => {
            let a: f64 = value.parse().unwrap_or(0.0);
            let b: f64 = filter_val.parse().unwrap_or(0.0);
            a <= b
        }
        "range" => {
            let a: f64 = value.parse().unwrap_or(0.0);
            let lo: f64 = filter_val.parse().unwrap_or(0.0);
            let hi: f64 = filter.value_to.as_deref().unwrap_or("0").parse().unwrap_or(0.0);
            a >= lo && a <= hi
        }
        _ => true,
    }
}
