export interface DatabaseConnection {
  id: string;
  name: string;
  path: string;
  lastConnectedAt?: string;
  lastConnectionError?: string;
}

export interface AppSettings {
  theme: 'light' | 'dark' | 'system';
  language: 'en' | 'zh-CN';
  enableMica: boolean;
}

export interface FieldMeta {
  name: string;
  typeCode: FieldTypeCode;
  size: number;
  isNullable: boolean;
}

export enum FieldTypeCode {
  Unknown = 0,
  Int32 = 1,
  Boolean = 2,
  Decimal = 3,
  DateTime = 4,
  String = 5,
  Enum = 6
}

export type RecordRow = Record<string, string>;

export interface TableDataResult {
  tableName: string;
  fieldNames: string[];
  records: RecordRow[];
  totalCount: number;
  page: number;
  pageSize: number;
  fallbackReason?: string;
}

export interface FilterRequest {
  field: string;
  operator: FilterOperator;
  value: string;
  valueTo?: string;
}

export type FilterOperator = 'contains' | 'equals' | 'starts_with' | 'ends_with' | 'gt' | 'lt' | 'gte' | 'lte' | 'range';

export const FILTER_OPERATORS: { key: FilterOperator; labelKey: string }[] = [
  { key: 'contains', labelKey: 'filter.contains' },
  { key: 'equals', labelKey: 'filter.equals' },
  { key: 'starts_with', labelKey: 'filter.startsWith' },
  { key: 'ends_with', labelKey: 'filter.endsWith' },
  { key: 'gt', labelKey: 'filter.gt' },
  { key: 'lt', labelKey: 'filter.lt' },
  { key: 'gte', labelKey: 'filter.gte' },
  { key: 'lte', labelKey: 'filter.lte' },
  { key: 'range', labelKey: 'filter.range' },
];
