import type { FieldDto, JsonValue, RecordDto, ValueDto } from './contracts.generated'
export function fieldValue(values: ValueDto[], field: FieldDto, recordId: string): ValueDto {
  return values.find(v => v.fieldId === field.id && v.recordId === (field.scope === 'Shared' ? null : recordId)) ??
    { fieldId: field.id, recordId: field.scope === 'Shared' ? null : recordId, value: null, attached: field.scope !== 'Individual', version: 0 }
}
export function displayValue(field: FieldDto, value: JsonValue, relations: RecordDto[] = [], regions: { id: string; name: string }[] = []): string {
  if (value === null) return '—'
  if (field.binding === 'Resource') return regions.find(x => x.id === value)?.name ?? '—'
  if (field.kind === 'Relation') return relations.find(x => x.id === value)?.name ?? '關聯資料'
  if (field.kind === 'SingleSelect') return field.options.find(x => x.id === value)?.name ?? '—'
  if (field.kind === 'MultiSelect') return (value as string[]).map(id => field.options.find(x => x.id === id)?.name ?? '—').join('、')
  if (field.kind === 'Checkbox' || field.kind === 'Marker') return value ? '✓' : '否'
  if (field.kind === 'DateTime') return new Date(String(value)).toLocaleString('zh-TW', { hour12: false })
  return String(value)
}
export function compareValues(a: JsonValue, b: JsonValue): number {
  if (a === null) return b === null ? 0 : -1
  if (b === null) return 1
  return typeof a === 'number' && typeof b === 'number' ? a - b : String(a).localeCompare(String(b), 'zh-TW', { numeric: true })
}
