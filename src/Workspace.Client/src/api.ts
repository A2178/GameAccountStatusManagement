import type { ValueDto } from './contracts.generated'

let csrf = ''
export class ApiError extends Error {
  constructor(message: string, public current?: ValueDto | null) { super(message) }
}
export async function request<T>(url: string, options: RequestInit = {}): Promise<T> {
  let response: Response
  try { response = await fetch(url, { credentials: 'same-origin', ...options, headers: { 'Content-Type': 'application/json', ...(csrf ? { 'X-CSRF-TOKEN': csrf } : {}), ...options.headers } }) }
  catch { throw new ApiError('無法連接伺服器，請等待重新連線。') }
  if (!response.ok) {
    const body = await response.json().catch(() => ({ message: '伺服器目前無法處理要求。' }))
    throw new ApiError(body.message ?? `要求失敗（${response.status}）`, body.current)
  }
  return response.json() as Promise<T>
}
export async function ensureCsrf() { csrf = (await request<{ token: string }>('/api/session/csrf')).token }
export function write<T = unknown>(url: string, body: unknown, method = 'PUT') { return request<T>(url, { method, body: JSON.stringify(body) }) }
