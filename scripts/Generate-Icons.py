"""Build deterministic vector-style Windows icons; requires Pillow."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import io
import struct

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "windows" / "Tray" / "Assets"
OUT.mkdir(parents=True, exist_ok=True)
SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def render(size, paused=False):
    scale = size * 8 / 24
    side = size * 8
    image = Image.new("RGBA", (side, side))
    mask = Image.new("L", (side, side))
    md = ImageDraw.Draw(mask)
    box = lambda coords: tuple(round(v * scale) for v in coords)
    md.rounded_rectangle(box((1, 1, 23, 23)), radius=round(5.5 * scale), fill=255)
    start, end = ((121, 130, 151), (76, 85, 109)) if paused else ((119, 82, 247), (58, 72, 215))
    gradient = Image.new("RGBA", (side, side))
    gd = ImageDraw.Draw(gradient)
    for y in range(side):
        t = y / max(1, side - 1)
        color = tuple(round(a + (b - a) * t) for a, b in zip(start, end)) + (255,)
        gd.line((0, y, side, y), fill=color)
    image.paste(gradient, (0, 0), mask)
    d = ImageDraw.Draw(image)
    white = (255, 255, 255, 255)
    d.rounded_rectangle(box((5.2, 6.2, 15.5, 19.4)), radius=round(1.9 * scale), outline=white, width=round(1.7 * scale))
    d.rounded_rectangle(box((7.5, 4.3, 13.2, 8.1)), radius=round(1.3 * scale), fill=white)
    d.rounded_rectangle(box((8, 10.1, 12.6, 11.3)), radius=round(0.6 * scale), fill=white)
    # A compact, opaque cutout gives the action glyph a clean silhouette at 16px.
    cutout = (73, 82, 111, 255) if paused else (66, 72, 219, 255)
    d.rounded_rectangle(box((11.4, 12.4, 22.2, 18.4)), radius=round(1.5 * scale), fill=cutout)
    if paused:
        for x in (14.2, 18.2):
            d.rounded_rectangle(box((x, 12.8, x + 2, 18)), radius=round(0.65 * scale), fill=(255, 214, 129, 255))
    else:
        mint = (137, 255, 220, 255)
        width = round(2.15 * scale)
        points = [(12.3, 15.4), (20.4, 15.4)]
        d.line([box(p) for p in points], fill=mint, width=width)
        d.line([box(p) for p in [(17.6, 12.6), (20.4, 15.4), (17.6, 18.2)]], fill=mint, width=width, joint="curve")
        for x, y in [(12.3, 15.4), (20.4, 15.4), (17.6, 12.6), (17.6, 18.2)]:
            r = 1.075
            d.ellipse(box((x-r, y-r, x+r, y+r)), fill=mint)
    return image.resize((size, size), Image.Resampling.LANCZOS)


def write_ico(path, paused):
    frames = []
    for size in SIZES:
        output = io.BytesIO()
        render(size, paused).save(output, format="PNG")
        frames.append((size, output.getvalue()))
    offset = 6 + 16 * len(frames)
    header = struct.pack("<HHH", 0, 1, len(frames))
    directory, payload = [], []
    for size, data in frames:
        directory.append(struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset))
        payload.append(data)
        offset += len(data)
    path.write_bytes(header + b"".join(directory) + b"".join(payload))


for paused, name in [(False, "ClipboardBridge"), (True, "ClipboardBridge-Paused")]:
    write_ico(OUT / (name + ".ico"), paused)
    render(256, paused).save(OUT / (name + ".png"))

# A reference preview shows the real raster frames on both taskbar backgrounds.
preview = Image.new("RGB", (840, 300), "#f4f5f9")
draw = ImageDraw.Draw(preview)
draw.rounded_rectangle((20, 20, 410, 280), radius=18, fill="#202127")
draw.rounded_rectangle((430, 20, 820, 280), radius=18, fill="#ffffff")
for origin, color in [(20, "#d9dae2"), (430, "#55576b")]:
    draw.text((origin + 24, 40), "CLIPBOARD BRIDGE", fill=color)
    for i, size in enumerate((16, 24, 32, 48)):
        x = origin + 26 + i * 82
        for y, paused in [(92, False), (174, True)]:
            icon = render(size, paused)
            preview.paste(icon, (x, y), icon)
        draw.text((x, 242), str(size) + " px", fill=color)
preview.save(OUT / "icon-preview.png")
print("Built 9-resolution application and paused icons, plus preview.")
