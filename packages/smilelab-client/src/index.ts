export type Role = 'patient' | 'doctor' | 'assistant' | 'consultant' | 'clinic_manager' | 'regional_manager' | 'supplier' | 'platform_operator' | 'platform_admin';
export interface Context { userId: string; tenantId: string; organizationId: string; clinicId: string; roles: Role[]; environment: string }
export interface Clinic { id: string; name: string; organizationId: string }
export interface Doctor { id: string; name: string; active: boolean; canSchedule: boolean }
export interface Patient { id: string; displayName: string; version: number; createdAt: string; profile?: Record<string, unknown> }
export interface Slot { id: string; doctorId: string; startsAt: string; endsAt: string; available?: boolean; status?: string }
export interface Appointment { id: string; patientId: string; patientName?: string; doctorId: string; doctorName?: string; slotId: string; startsAt: string; endsAt: string; status: 'booked' | 'arrived' | 'completed' | 'cancelled' | 'no_show'; version: number; createdAt: string }
export interface ClinicalContent { chiefComplaint?: string; history?: string; examination?: string; assessment?: string; plan?: string; toothChart?: { tooth: number; finding?: string }[] }
export interface ClinicalRecord { id: string; patientId: string; authorId: string; status: 'draft' | 'signed'; version: number; content: ClinicalContent; previousId: string | null; createdAt: string; signedAt: string | null }
export interface Lead { id: string; patientId: string | null; originRef: string; title: string; stage: 'new' | 'contacted' | 'qualified' | 'won' | 'lost'; assignedTo: string | null; version: number; createdAt: string }
export interface Activity { id: string; leadId: string; type: 'call' | 'follow_up' | 'visit'; summary: string; dueAt: string; status: 'pending' | 'done' | 'cancelled'; version: number; completedAt?: string | null }
export interface OutboxEvent { id: string; eventType: string; aggregateId: string; status: 'pending' | 'processing' | 'delivered' | 'dead_letter'; attempts: number; nextAttempt: string; leaseUntil: string | null; hasError: boolean; createdAt: string; deliveredAt: string | null }
export interface AuditEntry { id: number; clinicId: string; actorId: string; action: string; resourceId: string; requestId: string; details: Record<string, unknown>; createdAt: string }
export interface OperationsSummary { clinicId: string; patients: number; bookedAppointments: number; draftRecords: number; integrationQueue: { status: string; count: number; oldestCreatedAt: string }[]; aiMode: string; storage: string }
export interface Login { token: string; userId: string; expiresIn: number; authMode: string; isNewUser: boolean }

export class ApiError extends Error {
  constructor(public status: number, message: string, public requestId: string | null) { super(message); this.name = 'ApiError' }
}

// 无内置密钥、不记录请求正文；业务 POST 的重试由调用者使用同一个幂等键控制。
export class SmilelabClient {
  constructor(private options: { baseUrl?: string; token: () => string | null; language?: () => string; timeoutMs?: number; fetch?: typeof fetch }) {}
  async request<T>(path: string, method = 'GET', body?: unknown, headers: Record<string, string> = {}, signal?: AbortSignal): Promise<T> {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), this.options.timeoutMs ?? 15000);
    const abort = () => controller.abort(signal?.reason);
    if (signal?.aborted) abort(); else signal?.addEventListener('abort', abort, { once: true });
    try {
      const token = this.options.token();
      const response = await (this.options.fetch ?? fetch)((this.options.baseUrl ?? '') + path, {
        method, body: body === undefined ? undefined : JSON.stringify(body), credentials: 'omit', signal: controller.signal,
        headers: { 'Content-Type': 'application/json', 'Accept-Language': this.options.language?.() ?? 'zh-Hans', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...headers },
      });
      const requestId = response.headers.get('X-Request-Id');
      let envelope: { code: number; message: string; data: T };
      try { envelope = await response.json() } catch { throw new ApiError(response.status, 'Invalid API response', requestId) }
      if (!response.ok || envelope.code !== 0) throw new ApiError(response.status, envelope.message, requestId);
      return envelope.data;
    } finally { clearTimeout(timer); signal?.removeEventListener('abort', abort) }
  }
  dso<T>(path: string, method = 'GET', body?: unknown, idempotencyKey?: string) {
    return this.request<T>('/api/dso/v1/' + path, method, body, idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : {});
  }
  mini<T>(path: string, method = 'GET', body?: unknown, headers?: Record<string, string>) { return this.request<T>('/api/v1/' + path, method, body, headers) }
}

export const newIdempotencyKey = () => crypto.randomUUID();
