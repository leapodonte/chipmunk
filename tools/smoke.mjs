#!/usr/bin/env node
/**
 * SmileCheckAI API 冒烟测试（零依赖，Node 18+）
 *
 * 前置条件：
 * 1. PostgreSQL / Redis 已启动，且已执行 dotnet run --project api/Admin.Api -- --init
 * 2. API 已运行：dotnet run --project api/Admin.Api（默认 http://localhost:5000）
 *
 * 用法：密钥通过环境变量提供（与服务端 appsettings.json 的 Security 节保持一致）：
 *   SMOKE_REQUEST_KEY / SMOKE_CLIENT_PWD_KEY node tools/smoke.mjs [baseUrl]
 */
import crypto from 'node:crypto'

const BASE = process.argv[2] || process.env.SMOKE_BASE || 'http://localhost:5000'
const REQUEST_KEY = process.env.SMOKE_REQUEST_KEY
const CLIENT_PWD_KEY = process.env.SMOKE_CLIENT_PWD_KEY

if (!REQUEST_KEY || !CLIENT_PWD_KEY) {
  console.error('缺少环境变量 SMOKE_REQUEST_KEY / SMOKE_CLIENT_PWD_KEY（值与服务端 appsettings.json 的 Security 节一致）')
  process.exit(1)
}

let adminSession = ''
let appSession = ''
const deviceId = `smoke-${crypto.randomBytes(8).toString('hex')}`

function hmac(message, key) {
  return crypto.createHmac('sha256', key).update(message, 'utf8').digest('hex')
}

function signPwd(pwd) {
  return hmac(pwd, CLIENT_PWD_KEY)
}

let passed = 0
let failed = 0

function check(name, condition, extra = '') {
  if (condition) {
    passed++
    console.log(`  ✓ ${name}`)
  } else {
    failed++
    console.log(`  ✗ ${name} ${extra}`)
  }
}

/** 已签名的 POST 请求（按路径自动使用 Admin-/App- 前缀） */
async function post(path, body, session = '') {
  const payload = JSON.stringify(body ?? {})
  const ts = Math.floor(Date.now() / 1000)
  const prefix = path.startsWith('/admin') ? 'Admin' : 'App'
  const headers = {
    'Content-Type': 'application/json',
    [`${prefix}-Timestamp`]: String(ts),
    [`${prefix}-Signature`]: hmac(`${payload}${ts}`, REQUEST_KEY),
  }
  if (session) {
    headers[`${prefix}-Session`] = session
  }
  const res = await fetch(`${BASE}${path}`, { method: 'POST', headers, body: payload })
  return res.json()
}

/** form 上传（跳过签名） */
async function uploadForm(path, fields, file) {
  const form = new FormData()
  for (const [k, v] of Object.entries(fields)) {
    form.append(k, v)
  }
  form.append('file', new Blob([file], { type: 'image/png' }), 'photo.png')
  const headers = {}
  if (appSession) headers['App-Session'] = appSession
  const res = await fetch(`${BASE}${path}`, { method: 'POST', headers, body: form })
  return res.json()
}

/** 生成一张最小 PNG（1x1 白色像素） */
function tinyPng() {
  return Buffer.from(
    '89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c4890000000d4944415478da63f8cfc0f01f0005050202edb58ea40000000049454e44ae426082',
    'hex',
  )
}

async function main() {
  console.log(`\n== SmileCheckAI 冒烟测试: ${BASE} ==\n`)

  //1. 管理后台登录
  console.log('[1] 管理后台登录')
  let r = await post('/admin/auth/login', { username: 'admin', password: signPwd('Admin@12345') })
  check('登录成功', r.code === 0 && r.data?.session, JSON.stringify(r))
  adminSession = r.data?.session ?? ''

  //2. 模板/题目
  console.log('[2] 自测模板/题目')
  r = await post('/admin/selftest/template-list', { page: 1, page_size: 10 }, adminSession)
  check('模板列表 4 个', r.code === 0 && r.data?.items?.length === 4, `got ${r.data?.items?.length}`)
  const comprehensive = r.data?.items?.find((t) => t.code === 'comprehensive')
  check('综合模板存在', !!comprehensive)

  r = await post('/admin/selftest/question-list', { template_id: comprehensive.id }, adminSession)
  check('综合模板 10 题', r.code === 0 && r.data?.items?.length === 10, `got ${r.data?.items?.length}`)
  const questions = r.data?.items ?? []
  check('题目带选项', questions.every((q) => (q.options?.length ?? 0) > 0))

  //3. 微笑类型/推荐规则
  console.log('[3] 微笑类型/推荐规则')
  r = await post('/admin/smiletype/list', { page: 1, page_size: 50 }, adminSession)
  check('微笑类型 30 种', r.code === 0 && Number(r.data?.total) === 30, `got ${r.data?.total}`)
  r = await post('/admin/recommend/rule-list', { page: 1, page_size: 50 }, adminSession)
  check('推荐规则 5 条', r.code === 0 && Number(r.data?.total) === 5, `got ${r.data?.total}`)

  //4. App 短信登录（8888）
  console.log('[4] App 短信登录')
  r = await post('/app/auth/sms/send', { phone: '13800138000', scene: 'login' })
  check('发送验证码', r.code === 0, JSON.stringify(r))
  r = await post('/app/auth/sms-login', { phone: '13800138000', sms_code: '8888', device_id: deviceId })
  check('验证码 8888 登录成功', r.code === 0 && r.data?.session_id, JSON.stringify(r))
  appSession = r.data?.session_id ?? ''
  r = await post('/app/auth/sms-login', { phone: '13800138000', sms_code: '9999' })
  check('错误验证码被拒绝', r.code === 403, `code=${r.code}`)

  //5. App 提交问卷
  console.log('[5] 提交问卷')
  r = await post('/app/selftest/templates', {})
  check('App 模板列表', r.code === 0 && r.data?.items?.length === 4)
  r = await post('/app/selftest/questions', { template_id: comprehensive.id })
  check('App 题目不含分值', r.code === 0 && r.data?.questions?.[0]?.options?.[0]?.score === undefined)
  const appQuestions = r.data?.questions ?? []
  //故意选择低分选项，验证评分与微笑类型判定
  const answers = appQuestions.map((q) => ({ question_id: q.id, option_id: q.options[q.options.length - 1].id }))
  r = await post('/app/selftest/submit', { template_id: comprehensive.id, answers, device_id: deviceId }, appSession)
  check('提交问卷', r.code === 0 && r.data?.record_id, JSON.stringify(r))
  const recordId = r.data?.record_id

  //6. 上传照片 + 创建分析任务
  console.log('[6] 照片上传与分析任务')
  const png = tinyPng()
  for (const slot of ['front', 'bite', 'side']) {
    r = await uploadForm('/app/analysis/photos', { record_id: recordId, slot, device_id: deviceId }, png)
    check(`上传 ${slot} 照片`, r.code === 0 && r.data?.url, JSON.stringify(r))
  }
  r = await post('/app/analysis/tasks', { record_id: recordId, device_id: deviceId }, appSession)
  check('创建分析任务', r.code === 0 && r.data?.task_id, JSON.stringify(r))
  const taskId = r.data?.task_id

  //7. 轮询至完成（约 15 秒）
  console.log('[7] 轮询分析任务')
  let status = null
  for (let i = 0; i < 30; i++) {
    await new Promise((resolve) => setTimeout(resolve, 1500))
    r = await post('/app/analysis/task-detail', { task_id: taskId }, appSession)
    status = r.data
    if (r.code === 0 && (status.status === 'completed' || status.status === 'failed')) break
  }
  check('任务完成', status?.status === 'completed', JSON.stringify(status?.status))

  //8. 报告
  console.log('[8] 报告内容')
  r = await post('/app/selftest/record-detail', { record_id: recordId, device_id: deviceId }, appSession)
  const report = r.data?.report
  check('报告已生成', !!report)
  check('总分在 0-100', report?.total_score >= 0 && report?.total_score <= 100, `score=${report?.total_score}`)
  check('五维解读齐全', (report?.dimensions ?? []).length === 5)
  check('微笑类型存在', !!report?.smile_type?.name, JSON.stringify(report?.smile_type))
  check('定制建议 3 条', (report?.suggestions ?? []).length === 3)
  check('照片维度已融合（非参考标记）', (report?.dimensions ?? []).every((d) => d.ref === false))

  //后台报告详情（问答还原/照片/评测结果）
  console.log('[8.5] 后台报告详情')
  r = await post('/admin/appuser/report-detail', { report_id: report.id }, adminSession)
  check('报告详情返回', r.code === 0 && !!r.data?.report_id, JSON.stringify(r))
  check('问答还原 10 题', (r.data?.answers ?? []).length === 10, `got ${(r.data?.answers ?? []).length}`)
  check('答案含题干与选项', !!r.data?.answers?.[0]?.question_title && !!r.data?.answers?.[0]?.option_label)
  check('照片 3 张', (r.data?.photos ?? []).length === 3, `got ${(r.data?.photos ?? []).length}`)
  check('照片 URL 带 /storage 前缀', (r.data?.photos ?? []).every((p) => (p.url ?? '').startsWith('/storage/')), JSON.stringify(r.data?.photos?.[0]?.url))
  const photoUrl = BASE + (r.data?.photos?.[0]?.url ?? '')
  const photoRes = await fetch(photoUrl)
  check('照片文件可访问', photoRes.ok, `${photoUrl} → ${photoRes.status}`)
  check('评测结果完整', !!r.data?.smile_type?.name && (r.data?.dimensions ?? []).length === 5)

  //9. 推荐
  console.log('[9] 内容推荐')
  r = await post('/app/content/recommend', { device_id: deviceId }, appSession)
  check('推荐返回内容', r.code === 0 && (r.data?.items?.length ?? 0) > 0, JSON.stringify(r.data?.items?.length))
  r = await post('/app/content/science', { page: 1, page_size: 10 })
  check('科普列表（游客）', r.code === 0 && Number(r.data?.total) >= 15, `total=${r.data?.total}`)

  //10. 我的报告列表 + 授权留痕
  console.log('[10] 报告列表与授权')
  r = await post('/app/selftest/records', { page: 1, page_size: 10 }, appSession)
  check('我的报告列表', r.code === 0 && Number(r.data?.total) >= 1, `total=${r.data?.total}`)
  r = await post('/app/user/consent', { type: 'health', version: '1.0.0', granted: true }, appSession)
  check('医疗授权留痕', r.code === 0, JSON.stringify(r))
  r = await post('/admin/consent/list', { page: 1, page_size: 10 }, adminSession)
  check('后台可查授权记录', r.code === 0 && Number(r.data?.total) >= 1, `total=${r.data?.total}`)

  //11. 分析任务后台
  console.log('[11] 后台任务管理')
  r = await post('/admin/task/list', { page: 1, page_size: 10 }, adminSession)
  check('任务列表', r.code === 0 && Number(r.data?.total) >= 1, `total=${r.data?.total}`)

  console.log(`\n== 结果: ${passed} 通过, ${failed} 失败 ==\n`)
  process.exit(failed > 0 ? 1 : 0)
}

main().catch((err) => {
  console.error('冒烟测试异常:', err)
  process.exit(1)
})
