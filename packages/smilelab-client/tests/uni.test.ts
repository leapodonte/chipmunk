import test from 'node:test';
import assert from 'node:assert/strict';
import { ApiError } from '../src/index.ts';
import { SmilelabUniClient, type UniTransport, type UploadGrant } from '../src/uni.ts';

function fixture() {
  const requests: Parameters<UniTransport['request']>[0][] = [];
  const uploads: Parameters<NonNullable<UniTransport['uploadFile']>>[0][] = [];
  let aborts = 0; let token: string | null = 'test-token'; let language = 'zh-Hans';
  const transport: UniTransport = { request: options => { requests.push(options); return { abort: () => { aborts++; options.fail({ errMsg: 'abort' }); } }; }, uploadFile: options => { uploads.push(options); return { abort: () => { aborts++; } }; } };
  const client = new SmilelabUniClient({ transport, token: () => token, language: () => language });
  return { client, requests, uploads, transport, setToken: (value: string | null) => { token = value; }, setLanguage: (value: string) => { language = value; }, aborts: () => aborts };
}
const respond = (request: Parameters<UniTransport['request']>[0], data: unknown = {}) => request.success({ statusCode: 200, data: { code: 0, message: 'ok', data } });

test('原生请求使用当前会话与语言，不依赖浏览器API或cookie', async () => {
  const f = fixture();
  const first = f.client.profile();
  assert.equal(f.requests[0].url, 'https://app.smilelab.ai/api/v1/patients/me');
  assert.equal(f.requests[0].header.Authorization, 'Bearer test-token');
  assert.equal(f.requests[0].withCredentials, false);
  assert.equal(f.requests[0].timeout, 15000);
  respond(f.requests[0], { id: 'synthetic' });
  assert.deepEqual(await first, { id: 'synthetic' });
  f.setToken(null); f.setLanguage('en');
  const second = f.client.profile();
  assert.equal(f.requests[1].header.Authorization, undefined);
  assert.equal(f.requests[1].header['Accept-Language'], 'en');
  respond(f.requests[1]); await second;
});

test('微信兼容PUT和预约幂等键/知情授权按正文传递', async () => {
  const f = fixture();
  const profile = { displayName: 'Synthetic', version: 2, profile: { allergies: 'Synthetic' } };
  const update = f.client.updateProfile(profile);
  assert.equal(f.requests[0].method, 'PUT'); assert.deepEqual(JSON.parse(f.requests[0].data!), profile);
  respond(f.requests[0]); await update;
  const booking = f.client.book('slot-test', false, 'stable-booking-key');
  assert.equal(f.requests[1].header['Idempotency-Key'], 'stable-booking-key');
  assert.deepEqual(JSON.parse(f.requests[1].data!), { slotId: 'slot-test', shareWithDoctor: false });
  respond(f.requests[1]); await booking;
});

test('错误保留HTTP状态与大小写无关请求ID，不自动重试', async () => {
  const f = fixture(); const promise = f.client.profile();
  f.requests[0].success({ statusCode: 409, data: { code: 409, message: 'Conflict' }, header: { 'x-request-ID': 'trace-synthetic' } });
  await assert.rejects(promise, error => error instanceof ApiError && error.status === 409 && error.requestId === 'trace-synthetic');
  assert.equal(f.requests.length, 1);
});

test('非JSON/无效响应和原生超时返回安全错误', async () => {
  const f = fixture(); const first = f.client.profile();
  f.requests[0].success({ statusCode: 502, data: '<html>upstream</html>' });
  await assert.rejects(first, error => error instanceof ApiError && error.status === 502);
  const second = f.client.profile();
  f.requests[1].fail({ errMsg: 'request:fail timeout https://private?signature=must-not-leak' });
  await assert.rejects(second, error => error instanceof ApiError && error.status === 0 && error.message === '请求超时');
});

test('取消终止原生请求，迟到回调不能覆盖取消结果', async () => {
  const f = fixture(); const task = f.client.request('/api/v1/patients/me');
  const rejected = assert.rejects(task.promise, error => error instanceof ApiError && error.message === '请求已取消');
  task.cancel(); task.cancel(); respond(f.requests[0], { forbidden: true });
  await rejected; assert.equal(f.aborts(), 1);
});

test('签名文件上传保持表单字段，不附带Bearer或开发密钥', async () => {
  const f = fixture();
  const grant: UploadGrant = { uploadUrl: 'https://app.smilelab.ai/storage/objects/test?signature=synthetic', objectKey: 'tenant/clinic/user/test.png', expiresIn: 300, maxSize: 10485760, method: 'POST', storageProvider: 'disk-mock', formData: { policy: 'synthetic-policy', signature: 'synthetic-signature' } };
  const upload = f.client.upload(grant, '/temporary/synthetic.png');
  assert.equal(f.uploads[0].name, 'file'); assert.equal(f.uploads[0].formData.key, grant.objectKey);
  assert.equal(f.uploads[0].formData.policy, 'synthetic-policy'); assert.equal('header' in f.uploads[0], false);
  f.uploads[0].success({ statusCode: 200, data: JSON.stringify({ code: 0, message: 'ok', data: { mediaId: 'synthetic-media' } }) });
  assert.deepEqual(await upload.promise, { mediaId: 'synthetic-media' });
  assert.throws(() => f.client.upload({ ...grant, uploadUrl: 'https://other.invalid/storage/objects/test' }, '/temporary/synthetic.png'));
});

test('日期查询编码，患者记录/撤销授权走真实mini路由', async () => {
  const f = fixture(); const range = f.client.slots({ doctorId: 'd_001', from: '2026-10-05T08:00:00+08:00', to: '2026-10-06T08:00:00+08:00' });
  assert.ok(f.requests[0].url.includes('%2B08%3A00')); respond(f.requests[0], []); await range;
  const revoke = f.client.revokeDoctor('d_001'); assert.equal(f.requests[1].method, 'DELETE'); respond(f.requests[1]); await revoke;
});

test('开发登录密钥由调用者显式提供，API源必须HTTPS', async () => {
  const f = fixture(); const login = f.client.demoLogin('demo:synthetic', 'test-development-key');
  assert.equal(f.requests[0].header['X-Dev-Key'], 'test-development-key'); respond(f.requests[0]); await login;
  assert.throws(() => new SmilelabUniClient({ transport: f.transport, token: () => null, baseUrl: 'http://app.smilelab.ai' }));
  assert.throws(() => f.client.request('/api/v1/../../outside'));
});


test('订单申请、模拟付款和签收保留唯一业务键与订单编号', async () => {
  const f = fixture();
  const body = { doctorId: 'd_001', productCode: 'retainer_pair', quantity: 1, requestText: 'Synthetic', shippingAddress: { recipient: 'Synthetic', phone: '0000', address: 'Synthetic' } };
  const order = f.client.requestOrder(body, 'request-key');
  assert.equal(f.requests[0].url, 'https://app.smilelab.ai/api/v1/orders'); assert.equal(f.requests[0].header['Idempotency-Key'], 'request-key');
  assert.deepEqual(JSON.parse(f.requests[0].data!), body); respond(f.requests[0], { id: 'order-test' }); await order;
  const payment = f.client.payDemo('order-test', 2, 'payment-key');
  assert.equal(f.requests[1].header['Idempotency-Key'], 'payment-key'); assert.deepEqual(JSON.parse(f.requests[1].data!), { version: 2, confirmSimulation: true }); respond(f.requests[1]); await payment;
  const delivery = f.client.confirmDelivery('order-test', 9, 'delivery-key');
  assert.equal(f.requests[2].url, 'https://app.smilelab.ai/api/v1/orders/order-test/actions/confirm_delivery'); assert.equal(f.requests[2].header['Idempotency-Key'], 'delivery-key'); respond(f.requests[2]); await delivery;
});
