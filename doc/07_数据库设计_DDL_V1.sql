-- =====================================================================
--  牙套之家（暂名）平台 · 数据库 DDL V1（M1 范围）
--  目标数据库：PostgreSQL 17
--  执行方式：createdb ytzj → psql -U ytzj -d ytzj -f 07_数据库设计_DDL_V1.sql
--  配套文档：05_M0详细设计.md（§5 后端 / §6 API）、03_小程序平台架构设计.md
--
--  约定
--   1. 主键统一 ULID，CHAR(26)，由应用层生成（ADR-008）。
--   2. 一个业务域一个 schema；禁止跨 schema JOIN 与跨 schema 外键（ADR-003）。
--      因此本文件中所有外键均只在同 schema 内声明；跨域引用只存 ID，不建 FK。
--   3. 公共字段：created_at / updated_at / deleted_at（软删除）。
--   4. 金额单位一律「分」，BIGINT；时间一律 TIMESTAMPTZ。
--   5. 枚举用 VARCHAR + CHECK，便于灰度加值，不用 PG ENUM（改值要 ALTER TYPE）。
-- =====================================================================

BEGIN;

-- ---------------------------------------------------------------------
-- 0. 通用
-- ---------------------------------------------------------------------
CREATE EXTENSION IF NOT EXISTS pg_trgm;      -- 医生/内容关键词模糊搜索

CREATE SCHEMA IF NOT EXISTS identity;        -- 身份与关系（含疗程）
CREATE SCHEMA IF NOT EXISTS assess;          -- 自测、问卷、模拟记录
CREATE SCHEMA IF NOT EXISTS content;         -- 内容（帖子/案例/评论）
CREATE SCHEMA IF NOT EXISTS ai;              -- AI 任务与插件
CREATE SCHEMA IF NOT EXISTS ops;             -- 配置、协议、审计、文件
CREATE SCHEMA IF NOT EXISTS care;            -- 依从性（M2，本文件末尾）
CREATE SCHEMA IF NOT EXISTS msg;             -- 消息（M2，本文件末尾）
CREATE SCHEMA IF NOT EXISTS trade;           -- 订单与供应链（M3，占位）
CREATE SCHEMA IF NOT EXISTS growth;          -- 积分与裂变（M3，占位）

-- updated_at 自动维护
CREATE OR REPLACE FUNCTION public.set_updated_at() RETURNS trigger AS $$
BEGIN
  NEW.updated_at = now();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;


-- =====================================================================
-- 1. schema identity —— 身份与关系
-- =====================================================================

-- 1.1 用户
CREATE TABLE identity.app_user (
  id               CHAR(26)     PRIMARY KEY,
  wx_openid        VARCHAR(64),
  wx_unionid       VARCHAR(64),
  phone_hash       CHAR(64),                       -- HMAC-SHA256(手机号)，唯一索引与查询用
  phone_cipher     TEXT,                           -- AES-GCM 密文，仅运营外呼时解密
  phone_masked     VARCHAR(16),                    -- 138****8000，可直接展示
  nickname         VARCHAR(64),
  avatar_key       VARCHAR(256),                   -- public 桶 key
  gender           SMALLINT     NOT NULL DEFAULT 0,-- 0 未知 1 男 2 女
  birth_year       SMALLINT,
  province         VARCHAR(32),
  city             VARCHAR(32),
  status           VARCHAR(16)  NOT NULL DEFAULT 'active',
  first_device_id  VARCHAR(64),                    -- 游客期设备号，用于数据认领
  source_channel   VARCHAR(32),                    -- qr_doctor|invite|search|ad|ops
  source_scene_id  CHAR(26),
  last_login_at    TIMESTAMPTZ,
  created_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),
  deleted_at       TIMESTAMPTZ,
  CONSTRAINT ck_app_user_status CHECK (status IN ('active','banned','deactivated'))
);
COMMENT ON TABLE  identity.app_user IS '平台用户（患者/医生/专家/运营共用一张表，角色见 user_role）';
COMMENT ON COLUMN identity.app_user.phone_hash IS '手机号 HMAC，密钥 PHONE_HASH_KEY，禁止用明文建索引';

CREATE UNIQUE INDEX uk_app_user_openid ON identity.app_user (wx_openid)
  WHERE wx_openid IS NOT NULL AND deleted_at IS NULL;
CREATE UNIQUE INDEX uk_app_user_phone  ON identity.app_user (phone_hash)
  WHERE phone_hash IS NOT NULL AND deleted_at IS NULL;
CREATE INDEX idx_app_user_device  ON identity.app_user (first_device_id) WHERE first_device_id IS NOT NULL;
CREATE INDEX idx_app_user_created ON identity.app_user (created_at DESC);
CREATE TRIGGER trg_app_user_updated BEFORE UPDATE ON identity.app_user
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 1.2 角色
CREATE TABLE identity.user_role (
  id          CHAR(26)    PRIMARY KEY,
  user_id     CHAR(26)    NOT NULL REFERENCES identity.app_user(id),
  role        VARCHAR(16) NOT NULL,
  status      VARCHAR(16) NOT NULL DEFAULT 'active',
  granted_by  CHAR(26),
  granted_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT ck_user_role_role   CHECK (role IN ('patient','doctor','expert','ops','admin')),
  CONSTRAINT ck_user_role_status CHECK (status IN ('active','revoked'))
);
CREATE UNIQUE INDEX uk_user_role ON identity.user_role (user_id, role);
CREATE INDEX idx_user_role_role ON identity.user_role (role) WHERE status = 'active';
CREATE TRIGGER trg_user_role_updated BEFORE UPDATE ON identity.user_role
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 1.3 用户偏好（注册问卷结果 + 派生标签，推荐算法输入）
CREATE TABLE identity.user_preference (
  id             CHAR(26)    PRIMARY KEY,
  user_id        CHAR(26)    NOT NULL REFERENCES identity.app_user(id),
  template_id    CHAR(26)    NOT NULL,             -- assess.template.id（kind='preference'）
  answers        JSONB       NOT NULL DEFAULT '[]'::jsonb,
  tags           JSONB       NOT NULL DEFAULT '[]'::jsonb, -- ["嘴凸","怕拔牙","预算1-2万"]
  finished_at    TIMESTAMPTZ,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX uk_user_preference ON identity.user_preference (user_id);
CREATE INDEX idx_user_preference_tags ON identity.user_preference USING GIN (tags);
CREATE TRIGGER trg_user_preference_updated BEFORE UPDATE ON identity.user_preference
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 1.4 机构/门诊
CREATE TABLE identity.clinic (
  id          CHAR(26)     PRIMARY KEY,
  name        VARCHAR(128) NOT NULL,
  province    VARCHAR(32),
  city        VARCHAR(32),
  district    VARCHAR(32),
  address     VARCHAR(256),
  lng         NUMERIC(10,6),
  lat         NUMERIC(10,6),
  phone       VARCHAR(32),
  intro       TEXT,
  images      JSONB        NOT NULL DEFAULT '[]'::jsonb,
  tags        JSONB        NOT NULL DEFAULT '[]'::jsonb,
  status      VARCHAR(16)  NOT NULL DEFAULT 'active',
  created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
  deleted_at  TIMESTAMPTZ,
  CONSTRAINT ck_clinic_status CHECK (status IN ('active','hidden'))
);
CREATE INDEX idx_clinic_city ON identity.clinic (province, city) WHERE deleted_at IS NULL;
CREATE INDEX idx_clinic_name_trgm ON identity.clinic USING GIN (name gin_trgm_ops);
CREATE TRIGGER trg_clinic_updated BEFORE UPDATE ON identity.clinic
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 1.5 医生资料
CREATE TABLE identity.doctor_profile (
  id               CHAR(26)     PRIMARY KEY,
  user_id          CHAR(26)     NOT NULL REFERENCES identity.app_user(id),
  clinic_id        CHAR(26)     REFERENCES identity.clinic(id),
  real_name        VARCHAR(32)  NOT NULL,
  title            VARCHAR(32),                   -- 主治医师/副主任医师...
  education        VARCHAR(32),                   -- 本科/硕士/博士
  gender           SMALLINT     NOT NULL DEFAULT 0,
  birth_year       SMALLINT,
  province         VARCHAR(32),
  city             VARCHAR(32),
  specialties      JSONB        NOT NULL DEFAULT '[]'::jsonb, -- ["隐形矫正","舌侧","儿童早矫","美学"]
  intro            TEXT,
  years_practice   SMALLINT,
  verified         BOOLEAN      NOT NULL DEFAULT false,
  verified_at      TIMESTAMPTZ,
  scene_key        VARCHAR(20),                   -- 医生专属二维码 scene（≤32 字符限制，见设计文档 §8.6）
  patient_count    INTEGER      NOT NULL DEFAULT 0,
  case_count       INTEGER      NOT NULL DEFAULT 0,
  rank_score       NUMERIC(10,2) NOT NULL DEFAULT 0,
  status           VARCHAR(16)  NOT NULL DEFAULT 'active',
  created_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),
  deleted_at       TIMESTAMPTZ,
  CONSTRAINT ck_doctor_status CHECK (status IN ('active','hidden','banned'))
);
CREATE UNIQUE INDEX uk_doctor_user  ON identity.doctor_profile (user_id) WHERE deleted_at IS NULL;
CREATE UNIQUE INDEX uk_doctor_scene ON identity.doctor_profile (scene_key) WHERE scene_key IS NOT NULL;
CREATE INDEX idx_doctor_city   ON identity.doctor_profile (province, city) WHERE verified AND status='active';
CREATE INDEX idx_doctor_rank   ON identity.doctor_profile (rank_score DESC) WHERE verified AND status='active';
CREATE INDEX idx_doctor_spec   ON identity.doctor_profile USING GIN (specialties);
CREATE INDEX idx_doctor_name_trgm ON identity.doctor_profile USING GIN (real_name gin_trgm_ops);
CREATE TRIGGER trg_doctor_updated BEFORE UPDATE ON identity.doctor_profile
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 1.6 医生认证申请
CREATE TABLE identity.doctor_verification (
  id             CHAR(26)     PRIMARY KEY,
  user_id        CHAR(26)     NOT NULL REFERENCES identity.app_user(id),
  real_name      VARCHAR(32)  NOT NULL,
  id_card_cipher TEXT,                            -- 加密存储
  license_no     VARCHAR(64),                     -- 医师执业证编号
  clinic_name    VARCHAR(128),
  materials      JSONB        NOT NULL DEFAULT '[]'::jsonb, -- private 桶 key 列表
  status         VARCHAR(16)  NOT NULL DEFAULT 'pending',
  reviewer_id    CHAR(26),
  reject_reason  VARCHAR(256),
  reviewed_at    TIMESTAMPTZ,
  created_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
  CONSTRAINT ck_doctor_verify_status CHECK (status IN ('pending','approved','rejected'))
);
CREATE INDEX idx_doctor_verify_status ON identity.doctor_verification (status, created_at DESC);
CREATE INDEX idx_doctor_verify_user   ON identity.doctor_verification (user_id, created_at DESC);
CREATE TRIGGER trg_doctor_verify_updated BEFORE UPDATE ON identity.doctor_verification
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 1.7 疗程（医患关系的唯一单位，09-07 决策）
CREATE TABLE identity.course (
  id               CHAR(26)     PRIMARY KEY,
  patient_user_id  CHAR(26)     NOT NULL REFERENCES identity.app_user(id),
  doctor_user_id   CHAR(26)     REFERENCES identity.app_user(id),
  clinic_id        CHAR(26)     REFERENCES identity.clinic(id),
  type             VARCHAR(16)  NOT NULL DEFAULT 'consult',
  status           VARCHAR(16)  NOT NULL DEFAULT 'consulting',
  source           VARCHAR(16)  NOT NULL,
  scene_id         CHAR(26),
  started_at       TIMESTAMPTZ,
  ended_at         TIMESTAMPTZ,
  end_reason       VARCHAR(32),
  note             VARCHAR(512),
  created_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),
  deleted_at       TIMESTAMPTZ,
  CONSTRAINT ck_course_type   CHECK (type   IN ('consult','retainer','refine','ortho')),
  CONSTRAINT ck_course_status CHECK (status IN ('consulting','active','retention','finished','cancelled')),
  CONSTRAINT ck_course_source CHECK (source IN ('qr','invite','select','ops'))
);
COMMENT ON TABLE identity.course IS '疗程：一个患者与一个医生的一段治疗关系。医患多对多，但同一对同时只允许一个进行中疗程';

CREATE INDEX idx_course_patient ON identity.course (patient_user_id, status) WHERE deleted_at IS NULL;
CREATE INDEX idx_course_doctor  ON identity.course (doctor_user_id, status, created_at DESC) WHERE deleted_at IS NULL;
-- 同一患者-医生同时只允许一个未结束疗程（数据库层兜住业务规则）
CREATE UNIQUE INDEX uk_course_active ON identity.course (patient_user_id, doctor_user_id)
  WHERE status IN ('consulting','active','retention') AND deleted_at IS NULL;
CREATE TRIGGER trg_course_updated BEFORE UPDATE ON identity.course
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 1.8 疗程事件（状态变更留痕）
CREATE TABLE identity.course_event (
  id          CHAR(26)     PRIMARY KEY,
  course_id   CHAR(26)     NOT NULL REFERENCES identity.course(id),
  type        VARCHAR(32)  NOT NULL,   -- created|bound|activated|transferred|finished|cancelled|note
  actor_id    CHAR(26),
  payload     JSONB        NOT NULL DEFAULT '{}'::jsonb,
  created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE INDEX idx_course_event ON identity.course_event (course_id, created_at DESC);

-- 1.9 二维码场景（医生码 / 邀请码 / 活动码）
CREATE TABLE identity.qr_scene (
  id             CHAR(26)     PRIMARY KEY,
  scene_key      VARCHAR(20)  NOT NULL,   -- 写入小程序码 scene，≤32 字符
  type           VARCHAR(16)  NOT NULL,   -- doctor|invite|campaign
  owner_user_id  CHAR(26),
  payload        JSONB        NOT NULL DEFAULT '{}'::jsonb,
  qr_key         VARCHAR(256),            -- 已生成的小程序码 public 桶 key
  scan_count     INTEGER      NOT NULL DEFAULT 0,
  bind_count     INTEGER      NOT NULL DEFAULT 0,
  status         VARCHAR(16)  NOT NULL DEFAULT 'active',
  expires_at     TIMESTAMPTZ,
  created_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
  CONSTRAINT ck_qr_type   CHECK (type   IN ('doctor','invite','campaign')),
  CONSTRAINT ck_qr_status CHECK (status IN ('active','disabled'))
);
CREATE UNIQUE INDEX uk_qr_scene_key ON identity.qr_scene (scene_key);
CREATE INDEX idx_qr_owner ON identity.qr_scene (owner_user_id, type);
CREATE TRIGGER trg_qr_scene_updated BEFORE UPDATE ON identity.qr_scene
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 1.10 邀请关系（裂变最多两层，且必须挂靠医生 —— 09-06 决策）
CREATE TABLE identity.invitation (
  id               CHAR(26)     PRIMARY KEY,
  inviter_user_id  CHAR(26)     NOT NULL REFERENCES identity.app_user(id),
  invitee_user_id  CHAR(26)     NOT NULL REFERENCES identity.app_user(id),
  doctor_user_id   CHAR(26)     NOT NULL REFERENCES identity.app_user(id),
  level            SMALLINT     NOT NULL,   -- 1=医生直邀  2=患者转邀（挂靠同一医生）
  scene_id         CHAR(26)     REFERENCES identity.qr_scene(id),
  status           VARCHAR(16)  NOT NULL DEFAULT 'valid',
  created_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),
  CONSTRAINT ck_invitation_level  CHECK (level IN (1,2)),           -- level=3 由服务层拒绝，DB 兜底
  CONSTRAINT ck_invitation_status CHECK (status IN ('valid','revoked')),
  CONSTRAINT ck_invitation_self   CHECK (inviter_user_id <> invitee_user_id)
);
CREATE UNIQUE INDEX uk_invitation_invitee ON identity.invitation (invitee_user_id) WHERE status = 'valid';
CREATE INDEX idx_invitation_inviter ON identity.invitation (inviter_user_id, created_at DESC);
CREATE INDEX idx_invitation_doctor  ON identity.invitation (doctor_user_id, created_at DESC);
CREATE TRIGGER trg_invitation_updated BEFORE UPDATE ON identity.invitation
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();


-- =====================================================================
-- 2. schema assess —— 自测、偏好问卷、模拟记录
-- =====================================================================

-- 2.1 问卷模板（kind 区分自测与注册偏好问卷，复用同一套题目结构）
CREATE TABLE assess.template (
  id                CHAR(26)     PRIMARY KEY,
  kind              VARCHAR(16)  NOT NULL DEFAULT 'selftest',
  code              VARCHAR(32)  NOT NULL,
  name              VARCHAR(64)  NOT NULL,
  description       VARCHAR(256),
  duration_minutes  SMALLINT     NOT NULL DEFAULT 3,
  cover_key         VARCHAR(256),
  theme_color       VARCHAR(16),
  sort              SMALLINT     NOT NULL DEFAULT 0,
  photo_required    SMALLINT     NOT NULL DEFAULT 1,  -- 至少强制上传几张照片（07-19 决策：≥1）
  status            VARCHAR(16)  NOT NULL DEFAULT 'active',
  created_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),
  CONSTRAINT ck_template_kind   CHECK (kind IN ('selftest','preference')),
  CONSTRAINT ck_template_status CHECK (status IN ('active','offline'))
);
CREATE UNIQUE INDEX uk_template_code ON assess.template (code);
CREATE INDEX idx_template_kind ON assess.template (kind, status, sort);
CREATE TRIGGER trg_template_updated BEFORE UPDATE ON assess.template
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 2.2 题目
CREATE TABLE assess.question (
  id           CHAR(26)     PRIMARY KEY,
  template_id  CHAR(26)     NOT NULL REFERENCES assess.template(id),
  title        VARCHAR(256) NOT NULL,
  dimension    VARCHAR(32),                 -- 整齐度/清洁度/咬合/美观/意向...
  type         VARCHAR(16)  NOT NULL DEFAULT 'single',
  sort         SMALLINT     NOT NULL DEFAULT 0,
  required     BOOLEAN      NOT NULL DEFAULT true,
  status       VARCHAR(16)  NOT NULL DEFAULT 'active',
  created_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
  CONSTRAINT ck_question_type CHECK (type IN ('single','multi','photo'))
);
CREATE INDEX idx_question_template ON assess.question (template_id, sort) WHERE status = 'active';
CREATE TRIGGER trg_question_updated BEFORE UPDATE ON assess.question
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 2.3 选项
CREATE TABLE assess.question_option (
  id           CHAR(26)     PRIMARY KEY,
  question_id  CHAR(26)     NOT NULL REFERENCES assess.question(id),
  label        VARCHAR(128) NOT NULL,
  sort         SMALLINT     NOT NULL DEFAULT 0,
  score        SMALLINT     NOT NULL DEFAULT 0,
  tags         JSONB        NOT NULL DEFAULT '[]'::jsonb,  -- 命中后追加的用户标签
  created_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at   TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE INDEX idx_option_question ON assess.question_option (question_id, sort);
CREATE TRIGGER trg_option_updated BEFORE UPDATE ON assess.question_option
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 2.4 作答记录
CREATE TABLE assess.record (
  id           CHAR(26)     PRIMARY KEY,
  user_id      CHAR(26),                    -- 游客期为 NULL，登录后认领回填
  device_id    VARCHAR(64),
  template_id  CHAR(26)     NOT NULL REFERENCES assess.template(id),
  status       VARCHAR(16)  NOT NULL DEFAULT 'draft',
  submitted_at TIMESTAMPTZ,
  created_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
  deleted_at   TIMESTAMPTZ,
  CONSTRAINT ck_record_status CHECK (status IN ('draft','submitted','analyzing','reported','failed'))
);
CREATE INDEX idx_record_user   ON assess.record (user_id, created_at DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_record_device ON assess.record (device_id, created_at DESC) WHERE user_id IS NULL AND deleted_at IS NULL;
CREATE TRIGGER trg_record_updated BEFORE UPDATE ON assess.record
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 2.5 答案
CREATE TABLE assess.answer (
  id           CHAR(26)     PRIMARY KEY,
  record_id    CHAR(26)     NOT NULL REFERENCES assess.record(id),
  question_id  CHAR(26)     NOT NULL,
  option_id    CHAR(26),
  text_value   VARCHAR(512),
  created_at   TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE INDEX idx_answer_record ON assess.answer (record_id);

-- 2.6 报告
CREATE TABLE assess.report (
  id                   CHAR(26)     PRIMARY KEY,
  record_id            CHAR(26)     NOT NULL REFERENCES assess.record(id),
  total_score          SMALLINT,                       -- 保留字段，前端弱化展示（09-03 决策）
  grade                VARCHAR(16),
  smile_type           JSONB,                          -- {code,name,description,tags[]}
  dimensions           JSONB        NOT NULL DEFAULT '[]'::jsonb,
  tags                 JSONB        NOT NULL DEFAULT '[]'::jsonb,
  suggestions          JSONB        NOT NULL DEFAULT '[]'::jsonb,
  disclaimer_version   VARCHAR(16),
  generated_by         VARCHAR(32)  NOT NULL DEFAULT 'rule',  -- rule|ai|doctor
  doctor_edited_by     CHAR(26),                       -- 医生修改 AI 预诊断（07-19 决策）
  doctor_edited_at     TIMESTAMPTZ,
  created_at           TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at           TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX uk_report_record ON assess.report (record_id);
CREATE INDEX idx_report_tags ON assess.report USING GIN (tags);
CREATE TRIGGER trg_report_updated BEFORE UPDATE ON assess.report
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 2.7 自测照片
CREATE TABLE assess.photo (
  id          CHAR(26)     PRIMARY KEY,
  record_id   CHAR(26)     NOT NULL REFERENCES assess.record(id),
  slot        VARCHAR(16)  NOT NULL,      -- front|bite|side
  file_id     CHAR(26),                   -- ops.file_object.id
  oss_key     VARCHAR(256) NOT NULL,
  width       INTEGER,
  height      INTEGER,
  quality     JSONB        NOT NULL DEFAULT '{}'::jsonb,  -- 端侧质检结果：亮度/清晰度/人脸占比
  created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
  CONSTRAINT ck_photo_slot CHECK (slot IN ('front','bite','side','other'))
);
CREATE UNIQUE INDEX uk_photo_record_slot ON assess.photo (record_id, slot);

-- 2.8 AI 模拟记录（引流小游戏，09-02/09-03 决策）
CREATE TABLE assess.sim_record (
  id            CHAR(26)     PRIMARY KEY,
  user_id       CHAR(26),
  device_id     VARCHAR(64),
  capability    VARCHAR(32)  NOT NULL,     -- sim.frontal_align | sim.profile | sim.veneer | sim.whitening
  ai_task_id    CHAR(26)     NOT NULL,     -- ai.ai_task.id
  before_key    VARCHAR(256) NOT NULL,
  after_key     VARCHAR(256),
  params        JSONB        NOT NULL DEFAULT '{}'::jsonb,
  status        VARCHAR(16)  NOT NULL DEFAULT 'pending',
  share_key     VARCHAR(256),              -- 分享卡片 public 桶 key
  share_count   INTEGER      NOT NULL DEFAULT 0,
  created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
  deleted_at    TIMESTAMPTZ,
  CONSTRAINT ck_sim_status CHECK (status IN ('pending','running','succeeded','failed'))
);
CREATE INDEX idx_sim_user   ON assess.sim_record (user_id, created_at DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_sim_device ON assess.sim_record (device_id, created_at DESC) WHERE user_id IS NULL;
CREATE INDEX idx_sim_task   ON assess.sim_record (ai_task_id);
CREATE TRIGGER trg_sim_updated BEFORE UPDATE ON assess.sim_record
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();


-- =====================================================================
-- 3. schema content —— 内容（小红书式统一信息流，09-03 决策）
-- =====================================================================

-- 3.1 帖子（科普/案例/牙套日记/互助 统一模型）
CREATE TABLE content.post (
  id              CHAR(26)     PRIMARY KEY,
  author_user_id  CHAR(26)     NOT NULL,
  author_role     VARCHAR(16)  NOT NULL,   -- doctor|patient|official
  type            VARCHAR(16)  NOT NULL,   -- science|case|diary|help
  title           VARCHAR(128),
  body            TEXT,
  cover_key       VARCHAR(256),
  tags            JSONB        NOT NULL DEFAULT '[]'::jsonb,
  city            VARCHAR(32),
  status          VARCHAR(16)  NOT NULL DEFAULT 'pending',
  audit           JSONB        NOT NULL DEFAULT '{}'::jsonb,  -- 机审结果与人工意见
  like_count      INTEGER      NOT NULL DEFAULT 0,
  comment_count   INTEGER      NOT NULL DEFAULT 0,
  view_count      INTEGER      NOT NULL DEFAULT 0,
  hot_score       NUMERIC(12,4) NOT NULL DEFAULT 0,
  is_top          BOOLEAN      NOT NULL DEFAULT false,
  published_at    TIMESTAMPTZ,
  created_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
  deleted_at      TIMESTAMPTZ,
  CONSTRAINT ck_post_role   CHECK (author_role IN ('doctor','patient','official')),
  CONSTRAINT ck_post_type   CHECK (type IN ('science','case','diary','help')),
  CONSTRAINT ck_post_status CHECK (status IN ('draft','pending','published','rejected','removed'))
);
-- 发现流主查询：status='published' 按发布时间倒序
CREATE INDEX idx_post_feed   ON content.post (status, published_at DESC, id DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_post_role   ON content.post (author_role, status, published_at DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_post_type   ON content.post (type, status, published_at DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_post_author ON content.post (author_user_id, status, created_at DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_post_audit  ON content.post (status, created_at) WHERE status = 'pending';
CREATE INDEX idx_post_tags   ON content.post USING GIN (tags);
CREATE INDEX idx_post_title_trgm ON content.post USING GIN (title gin_trgm_ops);
CREATE TRIGGER trg_post_updated BEFORE UPDATE ON content.post
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 3.2 媒体
CREATE TABLE content.post_media (
  id                CHAR(26)     PRIMARY KEY,
  post_id           CHAR(26)     NOT NULL REFERENCES content.post(id),
  kind              VARCHAR(8)   NOT NULL,   -- image|video
  oss_key           VARCHAR(256) NOT NULL,
  cover_key         VARCHAR(256),
  width             INTEGER,
  height            INTEGER,
  duration_ms       INTEGER,
  size_bytes        BIGINT,
  transcode_status  VARCHAR(16)  NOT NULL DEFAULT 'none',
  sort              SMALLINT     NOT NULL DEFAULT 0,
  created_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),
  CONSTRAINT ck_media_kind CHECK (kind IN ('image','video')),
  CONSTRAINT ck_media_transcode CHECK (transcode_status IN ('none','pending','done','failed'))
);
CREATE INDEX idx_media_post ON content.post_media (post_id, sort);

-- 3.3 医生案例扩展（案例模板字段，09-02 决策）
CREATE TABLE content.case_detail (
  post_id          CHAR(26)     PRIMARY KEY REFERENCES content.post(id),
  treatment_type   VARCHAR(16),              -- invisible|lingual|labial|early
  duration_months  SMALLINT,
  age_range        VARCHAR(16),
  gender           SMALLINT,
  chief_complaint  VARCHAR(256),
  appliance        VARCHAR(64),
  before_keys      JSONB        NOT NULL DEFAULT '[]'::jsonb,
  after_keys       JSONB        NOT NULL DEFAULT '[]'::jsonb,
  doctor_note      TEXT,
  consent_key      VARCHAR(256),             -- 患者肖像授权凭证
  created_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at       TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE INDEX idx_case_type ON content.case_detail (treatment_type);
CREATE TRIGGER trg_case_updated BEFORE UPDATE ON content.case_detail
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 3.4 评论
CREATE TABLE content.post_comment (
  id          CHAR(26)     PRIMARY KEY,
  post_id     CHAR(26)     NOT NULL REFERENCES content.post(id),
  user_id     CHAR(26)     NOT NULL,
  parent_id   CHAR(26)     REFERENCES content.post_comment(id),
  body        VARCHAR(1000) NOT NULL,
  like_count  INTEGER      NOT NULL DEFAULT 0,
  status      VARCHAR(16)  NOT NULL DEFAULT 'published',
  created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
  deleted_at  TIMESTAMPTZ,
  CONSTRAINT ck_comment_status CHECK (status IN ('pending','published','rejected','removed'))
);
CREATE INDEX idx_comment_post   ON content.post_comment (post_id, created_at DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_comment_user   ON content.post_comment (user_id, created_at DESC);
CREATE INDEX idx_comment_parent ON content.post_comment (parent_id) WHERE parent_id IS NOT NULL;
CREATE TRIGGER trg_comment_updated BEFORE UPDATE ON content.post_comment
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 3.5 互动（点赞/收藏）
CREATE TABLE content.reaction (
  id           CHAR(26)     PRIMARY KEY,
  target_type  VARCHAR(16)  NOT NULL,   -- post|comment
  target_id    CHAR(26)     NOT NULL,
  user_id      CHAR(26)     NOT NULL,
  kind         VARCHAR(16)  NOT NULL,   -- like|favorite
  created_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
  CONSTRAINT ck_reaction_target CHECK (target_type IN ('post','comment')),
  CONSTRAINT ck_reaction_kind   CHECK (kind IN ('like','favorite'))
);
CREATE UNIQUE INDEX uk_reaction ON content.reaction (target_type, target_id, user_id, kind);
CREATE INDEX idx_reaction_user ON content.reaction (user_id, kind, created_at DESC);

-- 3.6 关注
CREATE TABLE content.follow (
  id                 CHAR(26)     PRIMARY KEY,
  follower_user_id   CHAR(26)     NOT NULL,
  followee_user_id   CHAR(26)     NOT NULL,
  created_at         TIMESTAMPTZ  NOT NULL DEFAULT now(),
  CONSTRAINT ck_follow_self CHECK (follower_user_id <> followee_user_id)
);
CREATE UNIQUE INDEX uk_follow ON content.follow (follower_user_id, followee_user_id);
CREATE INDEX idx_follow_followee ON content.follow (followee_user_id, created_at DESC);

-- 3.7 话题
CREATE TABLE content.topic (
  id          CHAR(26)     PRIMARY KEY,
  name        VARCHAR(32)  NOT NULL,
  slug        VARCHAR(32)  NOT NULL,
  intro       VARCHAR(256),
  post_count  INTEGER      NOT NULL DEFAULT 0,
  sort        SMALLINT     NOT NULL DEFAULT 0,
  status      VARCHAR(16)  NOT NULL DEFAULT 'active',
  created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX uk_topic_slug ON content.topic (slug);
CREATE TRIGGER trg_topic_updated BEFORE UPDATE ON content.topic
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

CREATE TABLE content.post_topic (
  post_id   CHAR(26) NOT NULL REFERENCES content.post(id),
  topic_id  CHAR(26) NOT NULL REFERENCES content.topic(id),
  PRIMARY KEY (post_id, topic_id)
);
CREATE INDEX idx_post_topic_topic ON content.post_topic (topic_id);

-- 3.8 审核日志
CREATE TABLE content.moderation_log (
  id           CHAR(26)     PRIMARY KEY,
  target_type  VARCHAR(16)  NOT NULL,   -- post|comment|media
  target_id    CHAR(26)     NOT NULL,
  engine       VARCHAR(16)  NOT NULL,   -- wx_text|wx_image|manual
  result       VARCHAR(16)  NOT NULL,   -- pass|risky|block
  detail       JSONB        NOT NULL DEFAULT '{}'::jsonb,
  operator_id  CHAR(26),
  created_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
  CONSTRAINT ck_moderation_result CHECK (result IN ('pass','risky','block'))
);
CREATE INDEX idx_moderation_target ON content.moderation_log (target_type, target_id, created_at DESC);


-- =====================================================================
-- 4. schema ai —— 插件注册与任务编排（09-07 决策：平台是整合者）
-- =====================================================================

-- 4.1 插件注册表
CREATE TABLE ai.plugin_registry (
  id           CHAR(26)     PRIMARY KEY,
  plugin_id    VARCHAR(64)  NOT NULL,
  type         CHAR(1)      NOT NULL,       -- A 异步任务 / B 跳转 / C 基础设施
  capability   VARCHAR(64)  NOT NULL,       -- sim.frontal_align / design.retainer / shop.mall ...
  name         VARCHAR(64)  NOT NULL,
  vendor       VARCHAR(64),
  version      VARCHAR(32)  NOT NULL DEFAULT 'v1',
  endpoint     VARCHAR(256),
  auth_type    VARCHAR(16)  NOT NULL DEFAULT 'hmac',
  auth_ref     VARCHAR(64),                 -- 指向环境变量名，密钥本身不入库
  timeout_ms   INTEGER      NOT NULL DEFAULT 60000,
  max_retry    SMALLINT     NOT NULL DEFAULT 2,
  billing      JSONB        NOT NULL DEFAULT '{}'::jsonb,  -- {"unit":"call","price":0.35,"currency":"CNY"}
  config       JSONB        NOT NULL DEFAULT '{}'::jsonb,
  enabled      BOOLEAN      NOT NULL DEFAULT false,
  gray_rate    SMALLINT     NOT NULL DEFAULT 100,
  priority     SMALLINT     NOT NULL DEFAULT 0,
  created_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
  CONSTRAINT ck_plugin_type  CHECK (type IN ('A','B','C')),
  CONSTRAINT ck_plugin_gray  CHECK (gray_rate BETWEEN 0 AND 100)
);
CREATE UNIQUE INDEX uk_plugin_id ON ai.plugin_registry (plugin_id);
CREATE INDEX idx_plugin_capability ON ai.plugin_registry (capability, enabled, priority DESC);
CREATE TRIGGER trg_plugin_updated BEFORE UPDATE ON ai.plugin_registry
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 4.2 任务
CREATE TABLE ai.ai_task (
  id                CHAR(26)     PRIMARY KEY,
  plugin_id         VARCHAR(64)  NOT NULL,
  capability        VARCHAR(64)  NOT NULL,
  user_id           CHAR(26),
  device_id         VARCHAR(64),
  biz_type          VARCHAR(32)  NOT NULL,   -- sim|assess_report|monitor_compare
  biz_id            CHAR(26),
  input             JSONB        NOT NULL DEFAULT '{}'::jsonb,
  params            JSONB        NOT NULL DEFAULT '{}'::jsonb,
  status            VARCHAR(16)  NOT NULL DEFAULT 'queued',
  progress          SMALLINT     NOT NULL DEFAULT 0,
  stage             VARCHAR(32),
  provider_task_id  VARCHAR(64),
  retry_count       SMALLINT     NOT NULL DEFAULT 0,
  error_code        VARCHAR(32),
  error_msg         VARCHAR(512),
  idempotency_key   VARCHAR(64),
  cost_ms           INTEGER,
  deadline_at       TIMESTAMPTZ,
  finished_at       TIMESTAMPTZ,
  created_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),
  CONSTRAINT ck_ai_task_status CHECK (status IN ('queued','running','succeeded','failed','timeout','cancelled'))
);
CREATE UNIQUE INDEX uk_ai_task_idem ON ai.ai_task (idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE INDEX idx_ai_task_user    ON ai.ai_task (user_id, created_at DESC);
CREATE INDEX idx_ai_task_status  ON ai.ai_task (status, created_at) WHERE status IN ('queued','running');
CREATE INDEX idx_ai_task_biz     ON ai.ai_task (biz_type, biz_id);
CREATE INDEX idx_ai_task_provider ON ai.ai_task (provider_task_id) WHERE provider_task_id IS NOT NULL;
CREATE TRIGGER trg_ai_task_updated BEFORE UPDATE ON ai.ai_task
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 4.3 结果
CREATE TABLE ai.ai_result (
  id          CHAR(26)     PRIMARY KEY,
  task_id     CHAR(26)     NOT NULL REFERENCES ai.ai_task(id),
  outputs     JSONB        NOT NULL DEFAULT '[]'::jsonb,  -- [{kind,oss_key,meta}]
  metrics     JSONB        NOT NULL DEFAULT '{}'::jsonb,  -- {duration_ms,model_version}
  created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX uk_ai_result_task ON ai.ai_result (task_id);

-- 4.4 计量（对账用）
CREATE TABLE ai.plugin_usage (
  id          CHAR(26)      PRIMARY KEY,
  plugin_id   VARCHAR(64)   NOT NULL,
  task_id     CHAR(26),
  user_id     CHAR(26),
  unit        VARCHAR(16)   NOT NULL DEFAULT 'call',
  quantity    NUMERIC(12,4) NOT NULL DEFAULT 1,
  unit_price  NUMERIC(12,4) NOT NULL DEFAULT 0,
  amount_cent BIGINT        NOT NULL DEFAULT 0,
  biz_ref     VARCHAR(64),
  success     BOOLEAN       NOT NULL DEFAULT true,
  created_at  TIMESTAMPTZ   NOT NULL DEFAULT now()
);
CREATE INDEX idx_usage_plugin_day ON ai.plugin_usage (plugin_id, created_at DESC);
CREATE INDEX idx_usage_task ON ai.plugin_usage (task_id);


-- =====================================================================
-- 5. schema ops —— 配置、协议、文件、审计
-- =====================================================================

-- 5.1 配置项
CREATE TABLE ops.app_config (
  id          CHAR(26)     PRIMARY KEY,
  config_key  VARCHAR(64)  NOT NULL,
  scope       VARCHAR(16)  NOT NULL DEFAULT 'app',   -- app|admin|feature
  value       JSONB        NOT NULL,
  remark      VARCHAR(256),
  updated_by  CHAR(26),
  created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX uk_app_config_key ON ops.app_config (config_key);
CREATE TRIGGER trg_app_config_updated BEFORE UPDATE ON ops.app_config
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 5.2 协议
CREATE TABLE ops.agreement (
  id            CHAR(26)     PRIMARY KEY,
  type          VARCHAR(16)  NOT NULL,   -- privacy|user|health|minor|disclaimer|photo_auth
  version       VARCHAR(16)  NOT NULL,
  title         VARCHAR(64)  NOT NULL,
  content       TEXT         NOT NULL,
  effective_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
  status        VARCHAR(16)  NOT NULL DEFAULT 'draft',
  created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
  CONSTRAINT ck_agreement_status CHECK (status IN ('draft','published','archived'))
);
CREATE UNIQUE INDEX uk_agreement ON ops.agreement (type, version);
CREATE INDEX idx_agreement_pub ON ops.agreement (type, status, effective_at DESC);
CREATE TRIGGER trg_agreement_updated BEFORE UPDATE ON ops.agreement
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 5.3 用户同意记录（敏感个人信息需单独同意，合规留证）
CREATE TABLE ops.user_agreement_accept (
  id            CHAR(26)     PRIMARY KEY,
  user_id       CHAR(26)     NOT NULL,
  agreement_id  CHAR(26)     NOT NULL,
  type          VARCHAR(16)  NOT NULL,
  version       VARCHAR(16)  NOT NULL,
  accepted_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
  ip            VARCHAR(64),
  client        VARCHAR(64)
);
CREATE UNIQUE INDEX uk_user_agreement ON ops.user_agreement_accept (user_id, type, version);
CREATE INDEX idx_user_agreement_user ON ops.user_agreement_accept (user_id);

-- 5.4 文件登记（所有 OSS 对象的唯一账本，孤儿文件清理依据）
CREATE TABLE ops.file_object (
  id             CHAR(26)     PRIMARY KEY,
  bucket         VARCHAR(64)  NOT NULL,
  oss_key        VARCHAR(256) NOT NULL,
  visibility     VARCHAR(8)   NOT NULL,   -- public|private
  biz_type       VARCHAR(32)  NOT NULL,   -- assess_photo|sim_input|post_image|doctor_verify...
  biz_id         CHAR(26),
  owner_user_id  CHAR(26),
  size_bytes     BIGINT,
  mime_type      VARCHAR(64),
  width          INTEGER,
  height         INTEGER,
  sha256         CHAR(64),
  status         VARCHAR(16)  NOT NULL DEFAULT 'pending',
  uploaded_at    TIMESTAMPTZ,
  created_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
  deleted_at     TIMESTAMPTZ,
  CONSTRAINT ck_file_visibility CHECK (visibility IN ('public','private')),
  CONSTRAINT ck_file_status     CHECK (status IN ('pending','uploaded','orphan','deleted'))
);
CREATE UNIQUE INDEX uk_file_key ON ops.file_object (bucket, oss_key);
CREATE INDEX idx_file_owner  ON ops.file_object (owner_user_id, created_at DESC);
CREATE INDEX idx_file_biz    ON ops.file_object (biz_type, biz_id);
CREATE INDEX idx_file_orphan ON ops.file_object (status, created_at) WHERE status = 'pending';
CREATE TRIGGER trg_file_updated BEFORE UPDATE ON ops.file_object
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 5.5 消息模板（微信订阅消息 / 短信 / 站内）
CREATE TABLE ops.msg_template (
  id              CHAR(26)     PRIMARY KEY,
  code            VARCHAR(32)  NOT NULL,
  channel         VARCHAR(16)  NOT NULL,   -- wx_subscribe|sms|inapp
  name            VARCHAR(64)  NOT NULL,
  wx_template_id  VARCHAR(64),
  sms_template_id VARCHAR(64),
  content         TEXT,
  params          JSONB        NOT NULL DEFAULT '[]'::jsonb,
  status          VARCHAR(16)  NOT NULL DEFAULT 'active',
  created_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
  CONSTRAINT ck_msg_channel CHECK (channel IN ('wx_subscribe','sms','inapp'))
);
CREATE UNIQUE INDEX uk_msg_template ON ops.msg_template (code, channel);
CREATE TRIGGER trg_msg_template_updated BEFORE UPDATE ON ops.msg_template
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 5.6 广告位 / Banner
CREATE TABLE ops.banner (
  id          CHAR(26)     PRIMARY KEY,
  position    VARCHAR(32)  NOT NULL,   -- home_top|home_mid|feed_insert
  title       VARCHAR(64),
  image_key   VARCHAR(256) NOT NULL,
  link_type   VARCHAR(16)  NOT NULL DEFAULT 'page',  -- page|post|doctor|webview
  link_value  VARCHAR(256),
  sort        SMALLINT     NOT NULL DEFAULT 0,
  start_at    TIMESTAMPTZ,
  end_at      TIMESTAMPTZ,
  status      VARCHAR(16)  NOT NULL DEFAULT 'active',
  created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE INDEX idx_banner_pos ON ops.banner (position, status, sort);
CREATE TRIGGER trg_banner_updated BEFORE UPDATE ON ops.banner
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- 5.7 审计日志（运营操作、医生查看患者数据、数据导出）
CREATE TABLE ops.audit_log (
  id           CHAR(26)     PRIMARY KEY,
  actor_id     CHAR(26),
  actor_role   VARCHAR(16),
  action       VARCHAR(64)  NOT NULL,
  target_type  VARCHAR(32),
  target_id    CHAR(26),
  detail       JSONB        NOT NULL DEFAULT '{}'::jsonb,
  ip           VARCHAR(64),
  trace_id     VARCHAR(32),
  created_at   TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE INDEX idx_audit_actor  ON ops.audit_log (actor_id, created_at DESC);
CREATE INDEX idx_audit_target ON ops.audit_log (target_type, target_id, created_at DESC);
CREATE INDEX idx_audit_time   ON ops.audit_log (created_at DESC);

-- 5.8 短信发送记录（风控与对账）
CREATE TABLE ops.sms_log (
  id          CHAR(26)     PRIMARY KEY,
  phone_hash  CHAR(64)     NOT NULL,
  phone_masked VARCHAR(16),
  scene       VARCHAR(32)  NOT NULL,
  template_id VARCHAR(64),
  provider_id VARCHAR(64),
  success     BOOLEAN      NOT NULL DEFAULT true,
  error_msg   VARCHAR(256),
  ip          VARCHAR(64),
  created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE INDEX idx_sms_phone ON ops.sms_log (phone_hash, created_at DESC);
CREATE INDEX idx_sms_ip    ON ops.sms_log (ip, created_at DESC);


-- =====================================================================
-- 6. 种子数据
-- =====================================================================

-- 6.1 配置
INSERT INTO ops.app_config (id, config_key, scope, value, remark) VALUES
('00000000000000000000CFG001','home','app',
 '{"title":"AI 口腔健康自测","guide_line1":"上传一张照片","guide_line2":"看看排齐后的自己","tested_count":"12.6万","disclaimer_selftest":"本自测不构成诊断意见","disclaimer_photo":"照片仅用于本次分析，可随时删除"}'::jsonb,
 '首页文案'),
('00000000000000000000CFG002','features','feature',
 '{"sim_frontal":true,"sim_profile":false,"feed":true,"checkin":false,"shop":false,"message":false}'::jsonb,
 'M1 功能开关：打卡与商城在 M2 打开'),
('00000000000000000000CFG003','limits','app',
 '{"sim_daily_limit_guest":1,"sim_daily_limit_user":3,"post_daily_limit":5,"comment_daily_limit":30}'::jsonb,
 '频次限制'),
('00000000000000000000CFG004','client','app',
 '{"min_client_version":"1.0.0","force_update":false}'::jsonb,
 '客户端版本控制');

-- 6.2 协议（正文由法务提供后替换，此处为占位）
INSERT INTO ops.agreement (id, type, version, title, content, status) VALUES
('00000000000000000000AGR001','privacy','1.0','隐私政策','【待法务定稿】说明收集范围、敏感个人信息（照片、口扫模型）的单独同意、存储期限、共享给设计与生产供应商的情形、用户删除与撤回途径。','published'),
('00000000000000000000AGR002','user','1.0','用户协议','【待法务定稿】平台性质、账号规则、内容规范、禁止行为、免责范围。','published'),
('00000000000000000000AGR003','health','1.0','健康信息使用说明','【待法务定稿】平台提供健康自测与信息服务，不提供诊断与治疗意见，不替代面诊。','published'),
('00000000000000000000AGR004','minor','1.0','未成年人监护人提示','【待法务定稿】儿童早矫自测需监护人同意并陪同使用。','published'),
('00000000000000000000AGR005','photo_auth','1.0','照片与口扫数据授权书','【待法务定稿】单独授权：用于生成分析报告、模拟效果、医生查看、订单设计与生产。','published'),
('00000000000000000000AGR006','disclaimer','1.0','AI 结果免责声明','本结果由 AI 生成，仅供参考，最终以医生面诊评审为准，不作为任何签约或治疗依据。','published');

-- 6.3 插件：mock 先行，LEO 正式服务到位后启用并停用 mock
INSERT INTO ai.plugin_registry
 (id, plugin_id, type, capability, name, vendor, endpoint, auth_ref, timeout_ms, billing, enabled, gray_rate, priority) VALUES
('00000000000000000000PGN001','mock-sim-frontal','A','sim.frontal_align','正面排齐模拟(本地Mock)','internal',
 'http://api:8080/mock/plugin','PLUGIN_MOCK_SECRET',10000,'{"unit":"call","price":0}'::jsonb,true,100,10),
('00000000000000000000PGN002','leo-sim-frontal-v1','A','sim.frontal_align','正面排齐模拟','leo',
 NULL,'PLUGIN_SIM_SECRET',60000,'{"unit":"call","price":0.35,"currency":"CNY"}'::jsonb,false,100,100),
('00000000000000000000PGN003','wx-content-security','C','moderation.text_image','微信内容安全','tencent',
 NULL,'WX_SECRET',5000,'{"unit":"call","price":0}'::jsonb,true,100,100);

-- 6.4 自测模板（07-19 决策：4 张卡片，≤10 题，至少强制 1 张照片）
INSERT INTO assess.template (id, kind, code, name, description, duration_minutes, sort, photo_required, status) VALUES
('00000000000000000000TMP001','selftest','comprehensive','牙齿综合自测','了解你的牙齿整体状况与改善方向',3,1,1,'active'),
('00000000000000000000TMP002','selftest','profile','嘴凸与侧脸自测','看看侧脸线条与牙齿的关系',3,2,1,'active'),
('00000000000000000000TMP003','selftest','crowding','牙齿拥挤自测','评估拥挤程度与常见方案',3,3,1,'active'),
('00000000000000000000TMP004','selftest','child','儿童早矫自测','儿童牙齿发育与早期干预时机',3,4,1,'active'),
('00000000000000000000TMP005','preference','onboarding','注册偏好问卷','了解你的关注点，为你推荐合适的内容与方案',2,0,0,'active');

COMMIT;


-- =====================================================================
-- 7. 第二阶段预建（M2 开始前单独执行，不属于 M1 交付）
--    保留在本文件是为了让 M1 的接口与命名提前对齐，避免二次改名。
-- =====================================================================
/*
BEGIN;

-- 7.1 佩戴周期（一副牙套 = 一个周期）
CREATE TABLE care.wear_period (
  id             CHAR(26)     PRIMARY KEY,
  course_id      CHAR(26)     NOT NULL,
  seq_no         SMALLINT     NOT NULL,          -- 第几副
  planned_days   SMALLINT     NOT NULL DEFAULT 14,
  planned_start  TIMESTAMPTZ  NOT NULL,
  planned_end    TIMESTAMPTZ  NOT NULL,
  actual_start   TIMESTAMPTZ,
  actual_end     TIMESTAMPTZ,
  status         VARCHAR(16)  NOT NULL DEFAULT 'pending',
  created_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at     TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX uk_wear_period ON care.wear_period (course_id, seq_no);

-- 7.2 打卡事件（摘/戴双打卡，09-02 决策）
CREATE TABLE care.checkin_event (
  id          CHAR(26)     PRIMARY KEY,
  course_id   CHAR(26)     NOT NULL,
  period_id   CHAR(26),
  user_id     CHAR(26)     NOT NULL,
  action      VARCHAR(8)   NOT NULL,     -- on 戴上 / off 摘下
  occurred_at TIMESTAMPTZ  NOT NULL,
  source      VARCHAR(16)  NOT NULL DEFAULT 'manual',  -- manual|backfill
  created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
  CONSTRAINT ck_checkin_action CHECK (action IN ('on','off'))
);
CREATE INDEX idx_checkin_course ON care.checkin_event (course_id, occurred_at DESC);

-- 7.3 佩戴评分（总时长 60% + 连续性 30% + 均衡性 10%，09-02 决策）
CREATE TABLE care.wear_score (
  id             CHAR(26)      PRIMARY KEY,
  course_id      CHAR(26)      NOT NULL,
  period_id      CHAR(26),
  stat_date      DATE          NOT NULL,
  wear_minutes   INTEGER       NOT NULL DEFAULT 0,
  duration_score NUMERIC(5,2)  NOT NULL DEFAULT 0,
  continuity_score NUMERIC(5,2) NOT NULL DEFAULT 0,
  evenness_score NUMERIC(5,2)  NOT NULL DEFAULT 0,
  total_score    NUMERIC(5,2)  NOT NULL DEFAULT 0,
  created_at     TIMESTAMPTZ   NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX uk_wear_score ON care.wear_score (course_id, stat_date);

-- 7.4 换套任务与复诊监控照片
CREATE TABLE care.change_task (
  id          CHAR(26)     PRIMARY KEY,
  course_id   CHAR(26)     NOT NULL,
  period_id   CHAR(26)     NOT NULL,
  due_at      TIMESTAMPTZ  NOT NULL,
  notified_at TIMESTAMPTZ,
  done_at     TIMESTAMPTZ,
  status      VARCHAR(16)  NOT NULL DEFAULT 'pending',
  created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE INDEX idx_change_due ON care.change_task (status, due_at);

CREATE TABLE care.monitor_photo (
  id          CHAR(26)     PRIMARY KEY,
  course_id   CHAR(26)     NOT NULL,
  task_id     CHAR(26),
  slot        VARCHAR(16)  NOT NULL,
  oss_key     VARCHAR(256) NOT NULL,
  ai_task_id  CHAR(26),
  ai_labels   JSONB        NOT NULL DEFAULT '[]'::jsonb,  -- 脱套/不贴合/进度偏离
  doctor_note VARCHAR(512),
  created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE INDEX idx_monitor_course ON care.monitor_photo (course_id, created_at DESC);

-- 7.5 站内私信（1:1，09-07 决策）
CREATE TABLE msg.conversation (
  id             CHAR(26)     PRIMARY KEY,
  user_a_id      CHAR(26)     NOT NULL,
  user_b_id      CHAR(26)     NOT NULL,
  course_id      CHAR(26),
  last_message_id CHAR(26),
  last_at        TIMESTAMPTZ,
  created_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
  updated_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
  CONSTRAINT ck_conv_pair CHECK (user_a_id < user_b_id)   -- 有序存放，保证唯一
);
CREATE UNIQUE INDEX uk_conversation ON msg.conversation (user_a_id, user_b_id);

CREATE TABLE msg.message (
  id          CHAR(26)     PRIMARY KEY,
  conv_id     CHAR(26)     NOT NULL,
  sender_id   CHAR(26)     NOT NULL,
  kind        VARCHAR(8)   NOT NULL DEFAULT 'text',
  body        TEXT,
  oss_key     VARCHAR(256),
  status      VARCHAR(16)  NOT NULL DEFAULT 'sent',
  created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE INDEX idx_message_conv ON msg.message (conv_id, created_at DESC);

CREATE TABLE msg.notification (
  id          CHAR(26)     PRIMARY KEY,
  user_id     CHAR(26)     NOT NULL,
  type        VARCHAR(32)  NOT NULL,
  title       VARCHAR(128),
  body        VARCHAR(512),
  link        VARCHAR(256),
  read_at     TIMESTAMPTZ,
  created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE INDEX idx_notification_user ON msg.notification (user_id, created_at DESC);
CREATE INDEX idx_notification_unread ON msg.notification (user_id) WHERE read_at IS NULL;

COMMIT;
*/


-- =====================================================================
-- 8. 附录：MySQL 8 对照（若 ADR-003 改选 MySQL，按此表替换）
-- =====================================================================
--   CHAR(26)            → CHAR(26) CHARACTER SET ascii
--   TIMESTAMPTZ         → DATETIME(3)，应用层统一存 UTC
--   JSONB               → JSON（无 GIN 索引，需为高频过滤字段建生成列 + 普通索引）
--   部分唯一索引         → MySQL 不支持 WHERE 子句索引：
--                          uk_course_active 改为增加 active_flag 生成列
--                          （status IN (...) ? patient+doctor : NULL）后建唯一索引
--   gin_trgm_ops        → 全文索引 FULLTEXT，或接入独立搜索服务
--   schema              → MySQL 的 database，跨库禁 JOIN 规则不变
--   CREATE TRIGGER      → 用 ON UPDATE CURRENT_TIMESTAMP(3) 替代
--   注意：部分唯一索引缺失会让「同一患者-医生只能有一个进行中疗程」失去数据库兜底，
--        必须在服务层加分布式锁，否则并发扫码会产生重复疗程。
