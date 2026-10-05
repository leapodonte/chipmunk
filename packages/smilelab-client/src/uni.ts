import { ApiError, type Appointment, type ClinicalRecord, type Login, type Patient, type Slot } from './index.ts';

export interface NativeTask { abort(): void }
export interface NativeResponse { statusCode: number; data: unknown; header?: Record<string, unknown> }
export interface UniTransport {
  request(options: { url: string; method: 'GET' | 'POST' | 'PUT' | 'DELETE'; data?: string; header: Record<string, string>; timeout: number; dataType: 'json'; withCredentials: false; success: (result: NativeResponse) => void; fail: (error: { errMsg?: string }) => void }): NativeTask;
  uploadFile?(options: { url: string; filePath: string; name: string; formData: Record<string, string>; timeout: number; success: (result: NativeResponse) => void; fail: (error: { errMsg?: string }) => void }): NativeTask;
}
export interface UploadGrant { uploadUrl: string; objectKey: string; expiresIn: number; maxSize: number; method: 'POST'; storageProvider: 'disk-mock'; formData: Record<string, string> }
export interface UploadedMedia { objectKey: string; mediaId: string; size: number; sha256: string; url: string }
export interface Simulation { taskId: string; status: 'analyzing' | 'done' | 'quality_failed'; jobStatus: 'queued' | 'running' | 'succeeded' | 'failed' | 'cancelled'; progress: number; isMock: true; provider: 'mock'; beforeImage: string; afterImage: string; resultText: string; failureCode: string | null }
export interface Cancelable<T> { promise: Promise<T>; cancel(): void }
export interface PatientProfile { displayName: string; version: number; profile: { birthDate?: string; gender?: 'female' | 'male' | 'other' | 'unknown'; allergies?: string; medicalHistory?: string; emergencyContact?: { name: string; phone: string; relationship?: string } } }

// 原生 UniApp / 微信请求适配，不依赖 fetch、Response、AbortController 或浏览器存储。
export class SmilelabUniClient {
  private base: string;
  constructor(private options: { transport: UniTransport; token: () => string | null; language?: () => string; baseUrl?: string; timeoutMs?: number }) {
    this.base = (options.baseUrl ?? 'https://app.smilelab.ai').replace(/\/$/, '');
    if (!/^https:\/\/[A-Za-z0-9.-]+(?::[0-9]+)?$/.test(this.base)) throw new Error('API base must be an HTTPS origin');
  }
  private message(kind: 'network' | 'timeout' | 'cancel' | 'response') {
    const english = this.options.language?.().startsWith('en');
    return ({ network: english ? 'Network request failed' : '网络请求失败', timeout: english ? 'Request timed out' : '请求超时', cancel: english ? 'Request cancelled' : '请求已取消', response: english ? 'Invalid API response' : '接口响应无效' })[kind];
  }
  private perform<T>(start: (success: (response: NativeResponse) => void, fail: (error: { errMsg?: string }) => void) => NativeTask): Cancelable<T> {
    let task: NativeTask | undefined; let settled = false; let rejectPromise!: (error: unknown) => void;
    const promise = new Promise<T>((resolve, reject) => {
      rejectPromise = reject;
      const fail = (error: { errMsg?: string }) => { if (!settled) { settled = true; reject(new ApiError(0, this.message(/timeout/i.test(error.errMsg ?? '') ? 'timeout' : 'network'), null)); } };
      try {
        task = start(response => {
          if (settled) return;
          settled = true;
          const requestId = Object.entries(response.header ?? {}).find(([key]) => key.toLowerCase() === 'x-request-id')?.[1];
          try {
            const envelope = typeof response.data === 'string' ? JSON.parse(response.data) : response.data;
            if (!envelope || typeof envelope !== 'object' || !Number.isInteger(envelope.code)) throw new Error();
            if (response.statusCode < 200 || response.statusCode >= 300 || envelope.code !== 0)
              throw new ApiError(response.statusCode, typeof envelope.message === 'string' ? envelope.message : this.message('response'), typeof requestId === 'string' ? requestId : null);
            resolve(envelope.data as T);
          } catch (error) { reject(error instanceof ApiError ? error : new ApiError(response.statusCode, this.message('response'), typeof requestId === 'string' ? requestId : null)); }
        }, fail);
      } catch { fail({}); }
    });
    return { promise, cancel: () => { if (!settled) { settled = true; try { task?.abort(); } finally { rejectPromise(new ApiError(0, this.message('cancel'), null)); } } } };
  }
  private timeout() { const value = this.options.timeoutMs ?? 15000; return Number.isFinite(value) ? Math.max(1000, Math.min(60000, value)) : 15000; }
  request<T>(path: string, method: 'GET' | 'POST' | 'PUT' | 'DELETE' = 'GET', body?: unknown, extra: Record<string, string> = {}): Cancelable<T> {
    if (!path.startsWith('/api/v1/') || path.includes('..') || path.includes('#')) throw new Error('Expected a mini-program API path');
    const token = this.options.token();
    return this.perform<T>((success, fail) => this.options.transport.request({ url: this.base + path, method, data: body === undefined ? undefined : JSON.stringify(body), timeout: this.timeout(), dataType: 'json', withCredentials: false, header: { 'Content-Type': 'application/json', 'Accept-Language': this.options.language?.() ?? 'zh-Hans', ...(token ? { Authorization: 'Bearer ' + token } : {}), ...extra }, success, fail }));
  }
  mini<T>(path: string, method: 'GET' | 'POST' | 'PUT' | 'DELETE' = 'GET', body?: unknown, headers?: Record<string, string>) { return this.request<T>('/api/v1/' + path, method, body, headers).promise; }
  login(code: string) { return this.mini<Login>('auth/mp-login', 'POST', { code }); }
  demoLogin(identity: string, developmentKey: string) { return this.mini<Login>('auth/mp-login', 'POST', { code: identity }, { 'X-Dev-Key': developmentKey }); }
  logout() { return this.mini<{ ok: true }>('auth/logout', 'POST', {}); }
  profile() { return this.mini<Patient>('patients/me'); }
  updateProfile(body: PatientProfile) { return this.mini<Patient>('patients/me', 'PUT', body); }
  records(page = 1, pageSize = 20) { return this.mini<ClinicalRecord[]>(`patients/me/records?page=${page}&pageSize=${pageSize}`); }
  slots(query: { doctorId?: string; from?: string; to?: string; page?: number; pageSize?: number } = {}) {
    const params = Object.entries(query).map(([key, value]) => encodeURIComponent(key) + '=' + encodeURIComponent(String(value))).join('&');
    return this.mini<Slot[]>('slots' + (params ? '?' + params : ''));
  }
  book(slotId: string, shareWithDoctor: boolean, operationKey: string) { return this.mini<Appointment>('appointments', 'POST', { slotId, shareWithDoctor }, { 'Idempotency-Key': operationKey }); }
  appointments(page = 1, pageSize = 20) { return this.mini<Appointment[]>(`appointments/mine?page=${page}&pageSize=${pageSize}`); }
  cancelAppointment(id: string, version: number) { return this.mini<Appointment>('appointments/' + encodeURIComponent(id) + '/status', 'POST', { status: 'cancelled', version }); }
  careTeam() { return this.mini<{ doctorId: string; name: string; grantedAt: string }[]>('patients/me/care-team'); }
  authorizeDoctor(id: string) { return this.mini('patients/me/care-team/' + encodeURIComponent(id), 'POST', {}); }
  revokeDoctor(id: string) { return this.mini('patients/me/care-team/' + encodeURIComponent(id), 'DELETE'); }
  uploadGrant(scene: 'ai_photo' | 'checkin_photo' | 'avatar', ext: 'jpg' | 'jpeg' | 'png' | 'webp') { return this.mini<UploadGrant>('media/upload-token', 'POST', { scene, ext }); }
  upload(grant: UploadGrant, filePath: string): Cancelable<UploadedMedia> {
    if (!grant.uploadUrl.startsWith(this.base + '/storage/objects/')) throw new Error('Upload URL must belong to the configured API origin');
    const uploadFile = this.options.transport.uploadFile;
    if (!uploadFile) throw new Error('Native uploadFile transport is required');
    // 签名 URL 已授予短期上传权限，不向文件请求附带员工密钥或 Bearer token。
    return this.perform<UploadedMedia>((success, fail) => uploadFile.call(this.options.transport, { url: grant.uploadUrl, filePath, name: 'file', formData: { ...grant.formData, key: grant.objectKey }, timeout: this.timeout(), success, fail }));
  }
  orders() { return this.mini<import('./index.ts').SharedOrder[]>('orders'); }
  order(id: string) { return this.mini<import('./index.ts').SharedOrder>('orders/' + encodeURIComponent(id)); }
  requestOrder(body: { doctorId: string; productCode: string; quantity: number; requestText: string; shippingAddress: import('./index.ts').OrderAddress }, key: string) { return this.mini<import('./index.ts').SharedOrder>('orders', 'POST', body, { 'Idempotency-Key': key }); }
  payDemo(id: string, version: number, key: string) { return this.mini<import('./index.ts').SharedOrder>('orders/' + encodeURIComponent(id) + '/actions/pay_demo', 'POST', { version, confirmSimulation: true }, { 'Idempotency-Key': key }); }
  confirmDelivery(id: string, version: number, key: string) { return this.mini<import('./index.ts').SharedOrder>('orders/' + encodeURIComponent(id) + '/actions/confirm_delivery', 'POST', { version }, { 'Idempotency-Key': key }); }
  mediaUrl(id: string) { return this.mini<{ url: string; expiresIn: number }>('media/' + encodeURIComponent(id) + '/url'); }
  simulate(imageKey: string) { return this.mini<{ taskId: string; status: 'queued'; isMock: true }>('ai/simulations', 'POST', { imageKey }); }
  simulation(id: string) { return this.mini<Simulation>('ai/simulations/' + encodeURIComponent(id)); }
  cancelSimulation(id: string) { return this.mini('ai/simulations/' + encodeURIComponent(id) + '/cancel', 'POST', {}); }
}
