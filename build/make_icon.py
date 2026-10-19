"""Draws the Sonokinesis icon (editor/icon.svg as pixels) into sonokinesis.ico. python make_icon.py"""
from PIL import Image, ImageDraw

S = 1024  # drawn large, then scaled down for each icon size


def draw():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    k = S / 256
    d.rounded_rectangle([0, 0, S - 1, S - 1], radius=48 * k, fill=(11, 13, 18, 255))
    r, w = 86 * k, 10 * k
    d.ellipse([128 * k - r, 128 * k - r, 128 * k + r, 128 * k + r], outline=(51, 229, 255, 150), width=int(w))
    d.polygon([(128 * k, 52 * k), (196 * k, 176 * k), (60 * k, 176 * k)], fill=(255, 42, 109, 255))
    c, h = (128 * k, 174 * k), 34 * k  # the cyan diamond (a square turned 45 degrees)
    d.polygon([(c[0], c[1] - h), (c[0] + h, c[1]), (c[0], c[1] + h), (c[0] - h, c[1])], fill=(51, 229, 255, 255))
    return img


big = draw()
sizes = [16, 24, 32, 48, 64, 128, 256]
big.resize((256, 256), Image.LANCZOS).save("sonokinesis.ico", sizes=[(s, s) for s in sizes])
big.resize((256, 256), Image.LANCZOS).save("sonokinesis-256.png")
