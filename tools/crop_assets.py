# -*- coding: utf-8 -*-
"""
从设计稿 PNG 裁剪 app 静态素材（吉祥物/插画/tabbar 图标）。
后续拿到 MasterGo 正式切图后，可直接替换 app/src/static/img/ 下同名文件。
用法: python tools/crop_assets.py
"""
import os
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'doc', '设计稿')
DST = os.path.join(ROOT, 'app', 'src', 'static', 'img')

# (来源文件, 输出名, (left, top, right, bottom))
CROPS = [
    # 首页 hero 吉祥物（拿放大镜的牙齿）
    ('首页.png', 'hero-mascot.png', (295, 135, 521, 330)),
    # 问卷页吉祥物（举盾牌的牙齿）
    ('牙齿自测2.png', 'quiz-mascot.png', (185, 175, 375, 365)),
    # 拍照引导页吉祥物（拿相机的牙齿+爱心）
    ('牙齿自测5.png', 'photo-intro-mascot.png', (145, 185, 365, 345)),
    # 分析中吉祥物（拿放大镜+数据面板）
    ('分析中.png', 'analyzing-mascot.png', (75, 300, 465, 660)),
    # 推荐页吉祥物（戴眼镜看书的牙齿）
    ('推荐.png', 'content-mascot.png', (325, 122, 495, 252)),
    # 模板卡插画（自测入口 4 张卡左侧圆角插画）
    ('牙齿自测1.png', 'tpl-comprehensive.png', (57, 287, 183, 403)),
    ('牙齿自测1.png', 'tpl-profile.png', (57, 437, 183, 553)),
    ('牙齿自测1.png', 'tpl-crowding.png', (57, 587, 183, 703)),
    ('牙齿自测1.png', 'tpl-kids.png', (57, 737, 183, 853)),
    # 拍摄助手取景框（正面/侧脸轮廓；内嵌提示胶囊由运行时状态胶囊覆盖）
    ('拍照1.png', 'frame-front.png', (38, 335, 495, 795)),
    ('拍照4.png', 'frame-side.png', (38, 335, 495, 795)),
    # 拍照引导页 3 张照片类型头像（圆形）
    ('牙齿自测5.png', 'photo-type-front.png', (38, 503, 112, 577)),
    ('牙齿自测5.png', 'photo-type-bite.png', (38, 630, 112, 704)),
    ('牙齿自测5.png', 'photo-type-side.png', (38, 755, 112, 829)),
    # TabBar 图标（选中态 + 未选中态，从不同截图取）
    ('首页.png', 'tab-home-active.png', (44, 1010, 88, 1046)),
    ('牙齿自测1.png', 'tab-home.png', (72, 1010, 118, 1046)),
    ('牙齿自测1.png', 'tab-selftest-active.png', (192, 1010, 238, 1046)),
    ('首页.png', 'tab-selftest.png', (174, 1010, 218, 1046)),
    ('推荐.png', 'tab-content-active.png', (295, 1020, 341, 1056)),
    ('首页.png', 'tab-content.png', (300, 1010, 344, 1046)),
    # 我的页截图无 tabbar，选中态由脚本染色生成
    ('首页.png', 'tab-mine.png', (428, 1010, 472, 1046)),
]


def make_tab_transparent(path: str):
    """tabbar 图标去浅色底（接近白/浅紫的像素转透明），并放大 2 倍"""
    im = Image.open(path).convert('RGBA')
    px = im.load()
    w, h = im.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            #浅色背景判定：亮度高且饱和度低
            if r > 225 and g > 225 and b > 235:
                px[x, y] = (255, 255, 255, 0)
    im = im.resize((w * 2, h * 2), Image.LANCZOS)
    im.save(path)


def tint_active(src: str, dst: str, color=(123, 108, 246)):
    """把未选中图标染成品牌紫，生成选中态图标"""
    im = Image.open(src).convert('RGBA')
    px = im.load()
    w, h = im.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a > 0:
                px[x, y] = (color[0], color[1], color[2], a)
    im.save(dst)


def normalize_tab_icons(dst: str = DST, canvas: int = 96, glyph: int = 90):
    """tabbar 图标归一化：统一画布、按最大边等比缩放并居中。

    各图标裁剪框大小不一（97x96/80x89/92x92/96x96），直接进 tabBar 会
    因 aspect-fit 缩放比例不同而显得大小不一、基线不齐；先归一化再使用。
    """
    import glob
    from PIL import Image as PILImage

    for path in sorted(glob.glob(os.path.join(dst, 'tab-*.png'))):
        im = PILImage.open(path).convert('RGBA')
        bbox = im.getbbox()
        if not bbox:
            continue
        im = im.crop(bbox)
        # 预乘 alpha 后缩放，避免边缘出现杂色光晕
        prem = PILImage.new('RGBA', im.size)
        prem_px = prem.load()
        im_px = im.load()
        for y in range(im.height):
            for x in range(im.width):
                r, g, b, a = im_px[x, y]
                prem_px[x, y] = (r * a // 255, g * a // 255, b * a // 255, a)
        scale = min(glyph / prem.width, glyph / prem.height)
        new_size = (max(1, round(prem.width * scale)), max(1, round(prem.height * scale)))
        prem = prem.resize(new_size, PILImage.LANCZOS)
        # 反预乘还原 RGB
        out = PILImage.new('RGBA', new_size)
        out_px = out.load()
        prem_px = prem.load()
        for y in range(new_size[1]):
            for x in range(new_size[0]):
                r, g, b, a = prem_px[x, y]
                if a > 0:
                    out_px[x, y] = (min(255, r * 255 // a), min(255, g * 255 // a), min(255, b * 255 // a), a)
        norm = PILImage.new('RGBA', (canvas, canvas), (0, 0, 0, 0))
        norm.alpha_composite(out, ((canvas - new_size[0]) // 2, (canvas - new_size[1]) // 2))
        norm.save(path)
        print(f'{os.path.basename(path)}: normalized {canvas}x{canvas} (glyph {new_size[0]}x{new_size[1]})')


def make_inverse_masks(dst: str = DST, canvas: int = 686):
    """生成实时取景遮罩（取反版）：人形/唇形内部完全透明露出摄像头画面，
    白色虚线轮廓保留为引导线，四周补成约 50% 黑色半透明。
    素材 alpha 结构：背景 0 / 内部磨砂白 128 / 虚线 255，以 128 为界做取反映射：
      a <= 128 -> 黑色半透明，alpha = 127 * (1 - a / 128)（背景 0 -> 127，边缘抗锯齿平滑过渡）
      a > 128  -> 白色引导线，alpha = 255 * (a - 128) / 127（内部 128 -> 0，虚线 255 -> 255）
    输出画布与 camera.vue 取景框等比（686x686 正方形），轮廓放大居中（占框宽约 75%，
    与设计稿一致）；正式切图替换 mask-*.png 后重跑本函数即可。
    """
    for name in ['mask-front.png', 'mask-mouth.png', 'mask-side.png']:
        src_path = os.path.join(dst, name)
        if not os.path.exists(src_path):
            print(f'skip {name}: not found')
            continue
        im = Image.open(src_path).convert('RGBA')
        px = im.load()
        for y in range(im.height):
            for x in range(im.width):
                r, g, b, a = px[x, y]
                if a <= 128:
                    px[x, y] = (0, 0, 0, round(127 * (1 - a / 128)))
                else:
                    px[x, y] = (255, 255, 255, round(255 * (a - 128) / 127))
        target_w = round(canvas * 0.75)
        scale = target_w / im.width
        new_size = (target_w, max(1, round(im.height * scale)))
        im = im.resize(new_size, Image.LANCZOS)
        out = Image.new('RGBA', (canvas, canvas), (0, 0, 0, 127))
        #直接 paste（而非 alpha_composite），保证镂空区 alpha=0 完全露出摄像头画面
        out.paste(im, ((canvas - new_size[0]) // 2, (canvas - new_size[1]) // 2))
        out_name = name.replace('.png', '-inverse.png')
        out.save(os.path.join(dst, out_name))
        print(f'{out_name}: {canvas}x{canvas} (hole {new_size[0]}x{new_size[1]})')


def main():
    os.makedirs(DST, exist_ok=True)
    for src_name, out_name, box in CROPS:
        src_path = os.path.join(SRC, src_name)
        im = Image.open(src_path)
        left, top, right, bottom = box
        left = max(0, min(left, im.width))
        top = max(0, min(top, im.height))
        right = max(left + 1, min(right, im.width))
        bottom = max(top + 1, min(bottom, im.height))
        crop = im.crop((left, top, right, bottom))
        out_path = os.path.join(DST, out_name)
        crop.save(out_path)
        if out_name.startswith('tab-'):
            make_tab_transparent(out_path)
        print(f'{out_name}: {crop.size[0]}x{crop.size[1]}')

    #生成「我的」选中态图标（染色）
    tint_active(os.path.join(DST, 'tab-mine.png'), os.path.join(DST, 'tab-mine-active.png'))
    print('tab-mine-active.png: tinted')

    #tabbar 图标统一画布与缩放，保证 active/inactive 大小、基线一致
    normalize_tab_icons()

    #实时取景遮罩取反版（人形透明、四周半透明）
    make_inverse_masks()


if __name__ == '__main__':
    main()
